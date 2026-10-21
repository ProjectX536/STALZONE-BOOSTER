using System.Text.Json;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Hags;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace Booster.Core.Tests.Hags;

public sealed class HagsExperimentServiceTests
{
    /// <summary>Два часа после загрузки <see cref="FakeBootTimeSource.DefaultBoot"/>.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly InMemoryStateFileStore _state = new();
    private readonly FakeHagsSource _hags = new();
    private readonly FakeBootTimeSource _boot = new();
    private readonly FakeBenchmarkStore _series = new(Series("a", Now.AddMinutes(-50)), Series("b", Now.AddMinutes(-40)));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HagsExperimentService Service() =>
        new(_state, _hags, _boot, _series, new FakeConfiguration(), _time, new ListLogger<HagsExperimentService>());

    private static BenchmarkSeries Series(string id, DateTimeOffset startedAt, string target = "game.exe", double fps = 100) =>
        SampleSeries.Completed(id, startedAt, target, fps);

    private static Reading<bool> Hags(bool enabled) => Reading<bool>.Known(enabled, SystemSamples.Source);

    private void RestartAt(DateTimeOffset boot) => _boot.Boot = Reading<DateTimeOffset>.Known(boot, "test-boot");

    /// <summary>Эксперимент по серии «b»; через час HAGS выключен и компьютер перезагружен.</summary>
    private async Task<HagsExperimentService> SwitchedAsync()
    {
        var service = Service();
        Assert.Null(await service.StartAsync(Ct));
        _time.Advance(TimeSpan.FromHours(1));
        _hags.Enabled = Hags(false);
        RestartAt(_time.GetUtcNow());
        Assert.Equal(HagsCheck.ReadyForCandidate, (await service.GetAsync(Ct)).Check);
        return service;
    }

    [Fact]
    public async Task Nothing_started_is_reported()
    {
        var view = await Service().GetAsync(Ct);

        Assert.Equal(new HagsExperimentView(null, HagsCheck.NotStarted, Hags(true), null, null), view);
    }

    [Fact]
    public async Task Start_takes_newest_completed_series_of_this_boot()
    {
        await _series.SaveAsync(Series("cancelled", Now.AddMinutes(-30)) with { Status = FrameCaptureStatus.Cancelled }, Ct);
        await _series.SaveAsync(Series("no-runs", Now.AddMinutes(-20)) with { Summary = null }, Ct);
        var service = Service();

        Assert.Null(await service.StartAsync(Ct));

        var view = await service.GetAsync(Ct);
        Assert.Equal(HagsCheck.WaitingForSwitch, view.Check);
        Assert.Equal(new HagsExperiment(1, Now, "game.exe", "b", BaselineEnabled: true, HagsExperimentStage.AwaitingSwitch, null, null, Now),
            view.Experiment);
        Assert.Null(view.Comparison);
        Assert.Null(view.Verdict);
    }

    [Fact]
    public async Task Start_refuses_series_from_before_boot()
    {
        RestartAt(Now.AddMinutes(-30));

        Assert.Equal(HagsStartRefusal.SeriesBeforeBoot, await Service().StartAsync(Ct));
        Assert.False(_state.Contains(HagsExperimentService.FileName));
    }

    [Fact]
    public async Task Start_refuses_without_completed_series()
    {
        await _series.DeleteAsync("a", Ct);
        await _series.DeleteAsync("b", Ct);
        await _series.SaveAsync(Series("failed", Now.AddMinutes(-10)) with { Status = FrameCaptureStatus.Failed }, Ct);

        Assert.Equal(HagsStartRefusal.NoCompletedSeries, await Service().StartAsync(Ct));
    }

    [Fact]
    public async Task Start_refuses_unknown_state()
    {
        _hags.Enabled = Reading<bool>.Unknown("HwSchMode", "value not set");

        Assert.Equal(HagsStartRefusal.StateUnknown, await Service().StartAsync(Ct));
        Assert.False(_state.Contains(HagsExperimentService.FileName));
    }

    /// <summary>Без времени загрузки не узнать, действует ли значение из реестра во время базовой серии.</summary>
    [Fact]
    public async Task Start_refuses_unknown_boot_time()
    {
        _boot.Boot = Reading<DateTimeOffset>.Unknown("test-boot", "query failed");

        Assert.Equal(HagsStartRefusal.StateUnknown, await Service().StartAsync(Ct));
    }

    /// <summary>HAGS переключили после загрузки, до базовой серии: в реестре не то значение, что действовало во время неё.</summary>
    [Fact]
    public async Task Start_refuses_value_changed_after_boot()
    {
        _hags.ChangedAt = Reading<DateTimeOffset>.Known(FakeBootTimeSource.DefaultBoot.AddMinutes(30), SystemSamples.Source);

        Assert.Equal(HagsStartRefusal.NeedsRestart, await Service().StartAsync(Ct));
        Assert.False(_state.Contains(HagsExperimentService.FileName));
    }

    [Fact]
    public async Task Start_refuses_unknown_change_time()
    {
        _hags.ChangedAt = Reading<DateTimeOffset>.Failure(SystemSamples.Source, "key cannot be read");

        Assert.Equal(HagsStartRefusal.StateUnknown, await Service().StartAsync(Ct));
        Assert.False(_state.Contains(HagsExperimentService.FileName));
    }

    [Fact]
    public async Task Start_refuses_when_already_started()
    {
        var service = Service();
        Assert.Null(await service.StartAsync(Ct));

        Assert.Equal(HagsStartRefusal.AlreadyStarted, await service.StartAsync(Ct));
    }

    [Fact]
    public async Task Unchanged_value_after_restart_waits_for_switch()
    {
        var service = Service();
        await service.StartAsync(Ct);
        _time.Advance(TimeSpan.FromHours(1));
        RestartAt(_time.GetUtcNow());

        Assert.Equal(HagsCheck.WaitingForSwitch, (await service.GetAsync(Ct)).Check);
    }

    [Fact]
    public async Task Changed_value_without_restart_needs_restart()
    {
        var service = Service();
        await service.StartAsync(Ct);
        _time.Advance(TimeSpan.FromMinutes(5));
        _hags.Enabled = Hags(false);
        await _series.SaveAsync(Series("too-early", _time.GetUtcNow()), Ct);

        var view = await service.GetAsync(Ct);

        Assert.Equal(HagsCheck.NeedsRestart, view.Check);
        Assert.Equal(HagsExperimentStage.AwaitingSwitch, view.Experiment!.Stage);
        Assert.Equal(HagsCandidateRefusal.NotReady, await service.TakeCandidateAsync(Ct));
    }

    /// <summary>
    /// Booster видел прежнее значение уже после перезагрузки; переключение после этого вступит в силу только со следующей
    /// загрузкой, хотя и она, и прошлая позже начала эксперимента.
    /// </summary>
    [Fact]
    public async Task Switch_after_seen_restart_needs_another_restart()
    {
        var service = Service();
        await service.StartAsync(Ct);
        _time.Advance(TimeSpan.FromHours(1));
        RestartAt(_time.GetUtcNow());
        Assert.Equal(HagsCheck.WaitingForSwitch, (await service.GetAsync(Ct)).Check);

        _time.Advance(TimeSpan.FromMinutes(10));
        _hags.Enabled = Hags(false);
        Assert.Equal(HagsCheck.NeedsRestart, (await service.GetAsync(Ct)).Check);

        _time.Advance(TimeSpan.FromMinutes(10));
        RestartAt(_time.GetUtcNow());
        var view = await service.GetAsync(Ct);
        Assert.Equal(HagsCheck.ReadyForCandidate, view.Check);
        Assert.Equal(_time.GetUtcNow(), view.Experiment!.SwitchedBootTime);
    }

    /// <summary>
    /// Перезагрузка прошла, пока Booster был закрыт, а HAGS переключили уже после неё: прежнее значение Booster не видел,
    /// но ключ записан позже загрузки, и новое значение вступит в силу только со следующей.
    /// </summary>
    [Fact]
    public async Task Switch_after_unseen_restart_needs_another_restart()
    {
        var service = Service();
        await service.StartAsync(Ct);
        _time.Advance(TimeSpan.FromHours(1));
        RestartAt(_time.GetUtcNow());
        _time.Advance(TimeSpan.FromMinutes(10));
        _hags.Enabled = Hags(false);
        _hags.ChangedAt = Reading<DateTimeOffset>.Known(_time.GetUtcNow(), SystemSamples.Source);

        var view = await service.GetAsync(Ct);
        Assert.Equal(HagsCheck.NeedsRestart, view.Check);
        Assert.Equal(HagsExperimentStage.AwaitingSwitch, view.Experiment!.Stage);

        _time.Advance(TimeSpan.FromMinutes(10));
        RestartAt(_time.GetUtcNow());
        view = await service.GetAsync(Ct);
        Assert.Equal(HagsCheck.ReadyForCandidate, view.Check);
        Assert.Equal(_time.GetUtcNow(), view.Experiment!.SwitchedBootTime);
    }

    [Fact]
    public async Task Unknown_change_time_after_switch_is_state_unknown()
    {
        var service = Service();
        await service.StartAsync(Ct);
        _time.Advance(TimeSpan.FromHours(1));
        _hags.Enabled = Hags(false);
        _hags.ChangedAt = Reading<DateTimeOffset>.Failure(SystemSamples.Source, "key cannot be read");
        RestartAt(_time.GetUtcNow());

        var view = await service.GetAsync(Ct);

        Assert.Equal(HagsCheck.StateUnknown, view.Check);
        Assert.Equal(HagsExperimentStage.AwaitingSwitch, view.Experiment!.Stage);
    }

    [Fact]
    public async Task Unknown_boot_time_after_switch_is_state_unknown()
    {
        var service = Service();
        await service.StartAsync(Ct);
        _hags.Enabled = Hags(false);
        _boot.Boot = Reading<DateTimeOffset>.Unknown("test-boot", "query failed");

        Assert.Equal(HagsCheck.StateUnknown, (await service.GetAsync(Ct)).Check);
    }

    [Fact]
    public async Task Changed_value_after_restart_waits_for_candidate()
    {
        await SwitchedAsync();

        // Новый экземпляр читает переход из файла, даже если время загрузки теперь неизвестно.
        _boot.Boot = Reading<DateTimeOffset>.Unknown("test-boot", "query failed");
        var view = await Service().GetAsync(Ct);

        Assert.Equal(HagsCheck.ReadyForCandidate, view.Check);
        Assert.Equal(HagsExperimentStage.AwaitingCandidate, view.Experiment!.Stage);
        Assert.Equal(Now.AddHours(1), view.Experiment.SwitchedBootTime);
        using var file = JsonDocument.Parse(_state.Files[HagsExperimentService.FileName]);
        Assert.Equal("AwaitingCandidate", file.RootElement.GetProperty("stage").GetString());
        Assert.Equal(1, file.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public async Task Candidate_must_be_same_target_after_switch()
    {
        var service = await SwitchedAsync();
        var switchedBoot = Now.AddHours(1);
        await _series.SaveAsync(Series("before-restart", switchedBoot.AddMinutes(-5)), Ct);
        await _series.SaveAsync(Series("other-game", switchedBoot.AddMinutes(20), target: "other.exe"), Ct);
        await _series.SaveAsync(Series("cancelled", switchedBoot.AddMinutes(30)) with { Status = FrameCaptureStatus.Cancelled }, Ct);

        Assert.Equal(HagsCandidateRefusal.NoCandidateSeries, await service.TakeCandidateAsync(Ct));

        await _series.SaveAsync(Series("candidate", switchedBoot.AddMinutes(10), target: "GAME.EXE"), Ct);
        Assert.Null(await service.TakeCandidateAsync(Ct));
        var view = await service.GetAsync(Ct);
        Assert.Equal(HagsCheck.Completed, view.Check);
        Assert.Equal("candidate", view.Experiment!.CandidateSeriesId);
    }

    [Fact]
    public async Task Switched_back_before_candidate_is_reported()
    {
        var service = await SwitchedAsync();
        await _series.SaveAsync(Series("candidate", Now.AddHours(1).AddMinutes(10)), Ct);
        _hags.Enabled = Hags(true);

        Assert.Equal(HagsCheck.SwitchedBack, (await service.GetAsync(Ct)).Check);
        Assert.Equal(HagsCandidateRefusal.SwitchedBack, await service.TakeCandidateAsync(Ct));
    }

    [Fact]
    public async Task Completed_compares_baseline_and_candidate()
    {
        var service = await SwitchedAsync();
        await _series.SaveAsync(Series("candidate", Now.AddHours(1).AddMinutes(10), fps: 110), Ct);
        Assert.Null(await service.TakeCandidateAsync(Ct));

        var view = await service.GetAsync(Ct);

        Assert.Equal(HagsCheck.Completed, view.Check);
        Assert.Equal(("b", "candidate"), (view.Comparison!.BaselineId, view.Comparison.CandidateId));
        Assert.Equal(ChangeVerdict.Improved, view.Comparison.AverageFpsVerdict);
        Assert.Equal(HagsVerdict.Keep, view.Verdict);
        // Серии и сравнение в файл не копируются: оно пересчитывается при каждом чтении.
        Assert.DoesNotContain("verdict", _state.Files[HagsExperimentService.FileName], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_baseline_series_is_reported()
    {
        var service = Service();
        await service.StartAsync(Ct);
        await _series.DeleteAsync("b", Ct);

        var view = await service.GetAsync(Ct);

        Assert.Equal(HagsCheck.BaselineMissing, view.Check);
        Assert.Equal("b", view.Experiment!.BaselineSeriesId);
        await service.ResetAsync(Ct);
        Assert.Equal(HagsCheck.NotStarted, (await service.GetAsync(Ct)).Check);
    }

    [Fact]
    public async Task Missing_candidate_series_is_reported()
    {
        var service = await SwitchedAsync();
        await _series.SaveAsync(Series("candidate", Now.AddHours(1).AddMinutes(10)), Ct);
        await service.TakeCandidateAsync(Ct);
        await _series.DeleteAsync("candidate", Ct);

        var view = await service.GetAsync(Ct);

        Assert.Equal(HagsCheck.CandidateMissing, view.Check);
        Assert.Null(view.Comparison);
    }

    [Fact]
    public async Task Written_by_hand_file_is_read()
    {
        _state.Put(HagsExperimentService.FileName, ExperimentFile());

        var view = await Service().GetAsync(Ct);

        Assert.Equal(HagsCheck.WaitingForSwitch, view.Check);
        Assert.Equal("b", view.Experiment!.BaselineSeriesId);
    }

    public static TheoryData<string> BrokenFiles => new()
    {
        "not json",
        "[]",
        ExperimentFile(schemaVersion: 2),
        """{ "schemaVersion": 1 }""",
        ExperimentFile(target: "\"\""),
        ExperimentFile(stage: "9"),
        ExperimentFile(stage: "\"AwaitingCandidate\""),
        ExperimentFile(stage: "\"Completed\"", switched: "\"2026-10-20T11:00:00+00:00\""),
    };

    [Theory]
    [MemberData(nameof(BrokenFiles))]
    public async Task Broken_file_is_unreadable_and_reset_deletes_it(string json)
    {
        _state.Put(HagsExperimentService.FileName, json);
        var service = Service();

        var view = await service.GetAsync(Ct);

        Assert.Equal(new HagsExperimentView(null, HagsCheck.Unreadable, Hags(true), null, null), view);
        Assert.Equal(HagsStartRefusal.AlreadyStarted, await service.StartAsync(Ct));
        Assert.Equal(HagsCandidateRefusal.NotReady, await service.TakeCandidateAsync(Ct));
        await service.ResetAsync(Ct);
        Assert.False(_state.Contains(HagsExperimentService.FileName));
        Assert.Equal(HagsCheck.NotStarted, (await service.GetAsync(Ct)).Check);
    }

    private static string ExperimentFile(int schemaVersion = 1, string target = "\"game.exe\"", string stage = "\"AwaitingSwitch\"", string switched = "null") =>
        $$"""
        {
          "schemaVersion": {{schemaVersion}},
          "startedAt": "2026-10-20T10:00:00+00:00",
          "target": {{target}},
          "baselineSeriesId": "b",
          "baselineEnabled": true,
          "stage": {{stage}},
          "switchedBootTime": {{switched}},
          "candidateSeriesId": null,
          "unchangedSeenAt": "2026-10-20T10:00:00+00:00"
        }
        """;
}
