using System.Globalization;
using Booster.App.Resources;
using Booster.App.Tests.TestSupport;
using Booster.App.ViewModels;
using Booster.Core.Abstractions;
using Booster.Core.Hags;
using Booster.Core.Reports;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Booster.App.Tests.ViewModels;

public sealed class HagsViewModelTests : IDisposable
{
    /// <summary>Два часа после загрузки <see cref="FakeBootTimeSource.DefaultBoot"/>.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);

    private const string NotSwitchedByBooster = "Booster HAGS не переключает";

    private readonly FakeTimeProvider _time = new(Now);
    private readonly InMemoryStateFileStore _state = new();
    private readonly FakeHagsSource _hags = new();
    private readonly FakeBootTimeSource _boot = new();
    private readonly FakeBenchmarkStore _series = new(SampleSeries.Completed("base", Now.AddMinutes(-40)));
    private readonly FakeSettingsOpener _opener = new();
    private readonly ScreenSession _screen = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    public void Dispose() => _screen.Dispose();

    private static Reading<bool> Hags(bool enabled) => Reading<bool>.Known(enabled, SystemSamples.Source);

    private HagsViewModel Panel() => new(new HagsExperimentService(_state, _hags, _boot, _series, new FakeConfiguration(), _time,
        NullLogger<HagsExperimentService>.Instance), _screen.Session, _opener, NullLogger<HagsViewModel>.Instance);

    private async Task<HagsViewModel> LoadedAsync()
    {
        var panel = Panel();
        await panel.RefreshAsync(Ct);
        return panel;
    }

    private async Task<HagsViewModel> StartedAsync()
    {
        var panel = await LoadedAsync();
        await panel.StartExperimentCommand.ExecuteAsync(null);
        Assert.Equal(HagsCheck.WaitingForSwitch, panel.View?.Check);
        return panel;
    }

    /// <summary>Тест начат по серии «base»; через час HAGS выключен и компьютер перезагружен.</summary>
    private async Task<HagsViewModel> SwitchedAsync()
    {
        var panel = await StartedAsync();
        _time.Advance(TimeSpan.FromHours(1));
        _hags.Enabled = Hags(false);
        _boot.Boot = Reading<DateTimeOffset>.Known(_time.GetUtcNow(), "test-boot");
        await panel.CheckCommand.ExecuteAsync(null);
        Assert.Equal(HagsCheck.ReadyForCandidate, panel.View?.Check);
        return panel;
    }

    private static void AssertOffered(HagsViewModel panel, bool start = false, bool candidate = false, bool reset = false)
    {
        Assert.Equal(start, panel.StartExperimentCommand.CanExecute(null));
        Assert.Equal(candidate, panel.TakeCandidateCommand.CanExecute(null));
        Assert.Equal(reset, panel.ResetCommand.CanExecute(null));
    }

    [Fact]
    public void Nothing_is_offered_before_the_state_is_read()
    {
        var panel = Panel();

        Assert.Null(panel.View);
        AssertOffered(panel);
        Assert.True(panel.CheckCommand.CanExecute(null));
    }

    [Fact]
    public async Task Not_started_offers_start()
    {
        var panel = await LoadedAsync();

        Assert.Equal(HagsCheck.NotStarted, panel.View?.Check);
        Assert.Equal(Strings.HagsStep_NotStarted, panel.StepText);
        Assert.Equal(string.Format(Culture, Strings.HagsCurrentFormat, Strings.HagsEnabled), panel.CurrentText);
        Assert.Null(panel.VerdictText);
        Assert.Null(panel.Status);
        AssertOffered(panel, start: true);
    }

    [Fact]
    public async Task Start_waits_for_the_users_switch()
    {
        var panel = await StartedAsync();

        Assert.Equal(Strings.HagsStarted, panel.Status);
        Assert.Equal(string.Format(Culture, Strings.HagsStep_WaitingForSwitch, Strings.HagsEnabled), panel.StepText);
        Assert.Contains(NotSwitchedByBooster, panel.StepText);
        AssertOffered(panel, reset: true);
        Assert.Empty(_opener.Pages);
    }

    [Fact]
    public void Open_settings_uses_advanced_graphics_page()
    {
        var panel = Panel();

        panel.OpenGraphicsSettingsCommand.Execute(null);

        Assert.Equal(["display-advancedgraphics"], _opener.Pages);
        Assert.Null(panel.Status);
    }

    [Fact]
    public void Settings_that_do_not_open_are_reported()
    {
        _opener.OpenResult = false;
        var panel = Panel();

        panel.OpenGraphicsSettingsCommand.Execute(null);

        Assert.Equal(Strings.SettingsOpenFailed, panel.Status);
    }

    [Fact]
    public async Task Needs_restart_explains_full_restart()
    {
        var panel = await StartedAsync();
        _hags.Enabled = Hags(false);

        await panel.CheckCommand.ExecuteAsync(null);

        Assert.Equal(HagsCheck.NeedsRestart, panel.View?.Check);
        Assert.Contains("«Перезагрузка»", panel.StepText);
        Assert.DoesNotContain("Завершение работы", panel.StepText);
        Assert.Equal(string.Format(Culture, Strings.HagsCurrentFormat, Strings.HagsDisabled), panel.CurrentText);
        AssertOffered(panel, reset: true);
    }

    [Fact]
    public async Task Ready_for_candidate_names_the_game_and_the_boot()
    {
        var panel = await SwitchedAsync();

        Assert.Equal(string.Format(Culture, Strings.HagsStep_ReadyForCandidate, Strings.HagsDisabled,
            _time.GetUtcNow().ToLocalTime().ToString("g", Culture), "game.exe"), panel.StepText);
        Assert.Null(panel.Status);
        AssertOffered(panel, candidate: true, reset: true);
    }

    [Fact]
    public async Task Candidate_refusal_names_the_game()
    {
        var panel = await SwitchedAsync();

        await panel.TakeCandidateCommand.ExecuteAsync(null);

        Assert.Equal(string.Format(Culture, Strings.HagsCandidateRefusal_NoCandidateSeries, "game.exe"), panel.Status);
        Assert.Equal(HagsCheck.ReadyForCandidate, panel.View?.Check);
    }

    [Fact]
    public async Task Completed_shows_verdict_and_manual_step()
    {
        var panel = await SwitchedAsync();
        var candidate = SampleSeries.Completed("candidate", _time.GetUtcNow().AddMinutes(10), fps: 80);
        // Изменение не описано — у сравнения есть оговорка.
        await _series.SaveAsync(candidate with
        {
            Conditions = candidate.Conditions with { Notes = candidate.Conditions.Notes with { Change = null } },
        }, Ct);

        await panel.TakeCandidateCommand.ExecuteAsync(null);

        var view = panel.View!;
        Assert.Equal(HagsCheck.Completed, view.Check);
        Assert.Equal(HagsVerdict.Revert, view.Verdict);
        Assert.Equal(Strings.HagsCandidateTaken, panel.Status);
        Assert.Equal(string.Format(Culture, Strings.HagsStep_Completed, Strings.HagsEnabled, Strings.HagsDisabled), panel.StepText);
        Assert.StartsWith(string.Format(Culture, Strings.HagsVerdict_Revert, Strings.HagsDisabled, Strings.HagsEnabled), panel.VerdictText);
        Assert.Contains("вручную в «Параметрах»", panel.VerdictText);
        Assert.Contains(NotSwitchedByBooster, panel.VerdictText);
        Assert.NotEmpty(view.Comparison!.Caveats);
        Assert.All(view.Comparison.Caveats, caveat => Assert.Contains(EnumText.Format(caveat), panel.VerdictText));
        AssertOffered(panel, reset: true);
    }

    [Fact]
    public async Task Start_refusal_is_shown()
    {
        _boot.Boot = Reading<DateTimeOffset>.Known(Now.AddMinutes(-30), "test-boot");
        var panel = await LoadedAsync();

        await panel.StartExperimentCommand.ExecuteAsync(null);

        Assert.Equal(Strings.HagsStartRefusal_SeriesBeforeBoot, panel.Status);
        Assert.Contains("запишите серию после перезагрузки", panel.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HagsCheck.NotStarted, panel.View?.Check);
        Assert.False(_state.Contains(HagsExperimentService.FileName));
    }

    [Fact]
    public async Task Start_refusal_after_change_since_boot_asks_for_restart()
    {
        _hags.ChangedAt = Reading<DateTimeOffset>.Known(FakeBootTimeSource.DefaultBoot.AddMinutes(5), SystemSamples.Source);
        var panel = await LoadedAsync();

        await panel.StartExperimentCommand.ExecuteAsync(null);

        Assert.Equal(Strings.HagsStartRefusal_NeedsRestart, panel.Status);
        Assert.Contains("«Перезагрузка»", panel.Status);
        Assert.Contains(NotSwitchedByBooster, panel.Status);
        Assert.False(_state.Contains(HagsExperimentService.FileName));
    }

    [Fact]
    public async Task Broken_file_offers_reset_only()
    {
        _state.Put(HagsExperimentService.FileName, "{");

        var panel = await LoadedAsync();

        Assert.Equal(HagsCheck.Unreadable, panel.View?.Check);
        Assert.Equal(Strings.HagsStep_Unreadable, panel.StepText);
        AssertOffered(panel, reset: true);
    }

    [Fact]
    public async Task Reset_returns_to_not_started()
    {
        var panel = await SwitchedAsync();

        await panel.ResetCommand.ExecuteAsync(null);

        Assert.Equal(HagsCheck.NotStarted, panel.View?.Check);
        Assert.Equal(Strings.HagsResetDone, panel.Status);
        Assert.False(_state.Contains(HagsExperimentService.FileName));
        AssertOffered(panel, start: true);
    }

    [Fact]
    public async Task Read_failure_offers_nothing_but_check()
    {
        _hags.Failure = new IOException("registry unavailable");

        var panel = await LoadedAsync();

        Assert.Null(panel.View);
        Assert.Null(panel.StepText);
        Assert.Equal(string.Format(Culture, Strings.HagsErrorFormat, "registry unavailable"), panel.Status);
        Assert.Null(_screen.Session.Hags);
        AssertOffered(panel);
        Assert.True(panel.CheckCommand.CanExecute(null));
    }

    [Fact]
    public async Task Every_refresh_updates_session()
    {
        var changes = 0;
        _screen.Session.Changed += (_, _) => changes++;

        var panel = await LoadedAsync();

        Assert.NotNull(panel.View);
        Assert.Same(panel.View, _screen.Session.Hags);
        Assert.Equal(1, changes);

        await panel.StartExperimentCommand.ExecuteAsync(null);

        Assert.Equal(HagsCheck.WaitingForSwitch, _screen.Session.Hags?.Check);
        Assert.Same(panel.View, _screen.Session.Hags);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Every_step_says_booster_does_not_switch_hags()
    {
        foreach (var check in Enum.GetValues<HagsCheck>())
            Assert.Contains(NotSwitchedByBooster, Strings.ResourceManager.GetString("HagsStep_" + check, Strings.Culture));
    }
}
