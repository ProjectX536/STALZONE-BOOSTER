using System.Globalization;
using Booster.App.Resources;
using Booster.App.Tests.TestSupport;
using Booster.App.ViewModels;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Inventory;
using Booster.Core.Metrics;
using Booster.Core.Profiles;
using Booster.Core.Tests.TestSupport;
using Booster.Platform.Windows.Monitoring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booster.App.Tests.ViewModels;

public sealed class PerformanceViewModelTests : IAsyncDisposable
{
    private readonly ScreenSession _screen = new();
    private readonly FakeSensorProvider _sensors;
    private readonly ListLogger<BenchmarkRecorder> _recorderLog = new();
    private readonly FakeFrameCaptureProvider _capture;
    private readonly FakeBenchmarkStore _store = new();
    private readonly FakeCuePlayer _cues = new();
    private readonly FakeGaming _gaming = GamingWithOneAction();
    private readonly PerformanceViewModel _performance;
    private BenchmarkSchedule _schedule = null!;

    public PerformanceViewModelTests()
    {
        _sensors = new FakeSensorProvider(_screen.Time);
        _capture = new FakeFrameCaptureProvider(_screen.Time) { Result = FramesInRuns };
        var recorder = new BenchmarkRecorder(_sensors, new FakeProcessActivitySource(), new FakeSystemPressureSource(), _screen.Config,
            _screen.Time, _recorderLog);
        var runner = new BenchmarkRunner(_capture, recorder, new NoDisplays(), new FakePowerPlanProvider(), _screen.Config, _screen.Time,
            NullLogger<BenchmarkRunner>.Instance);
        _performance = ScreenSession.OnUiContext(() => new PerformanceViewModel(_capture, new NoWindows(), _screen.Session, _screen.Report,
            runner, _store, _gaming.Coordinator, _cues, _screen.Config, _screen.Time, NullLogger<PerformanceViewModel>.Instance));
        _performance.UseWindowList = false;
        _performance.ExecutableName = "game";
        Configure(runs: 1, seconds: 10, pause: 0, delay: 3);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Series_fields_default_to_benchmark_settings()
    {
        var performance = ScreenSession.OnUiContext(() => new PerformanceViewModel(_capture, new NoWindows(), _screen.Session, _screen.Report,
            null!, _store, _gaming.Coordinator, _cues, _screen.Config, _screen.Time, NullLogger<PerformanceViewModel>.Instance));

        var defaults = FakeConfiguration.DefaultBenchmark;
        Assert.Equal(Text(defaults.RunCount), performance.RunCountText);
        Assert.Equal(Text(defaults.RunSeconds), performance.RunSecondsText);
        Assert.Equal(Text(defaults.PauseSeconds), performance.PauseSecondsText);
        Assert.Equal(Text(defaults.StartDelaySeconds), performance.StartDelayText);
        Assert.Equal(defaults.SoundCues, performance.SoundCues);
    }

    [Fact]
    public async Task Start_runs_the_configured_series_with_notes()
    {
        Configure(runs: 2, seconds: 10, pause: 2, delay: 3);
        _performance.GraphicsProfile = " Высокие ";
        _performance.Scene = "Город";
        _performance.WindowMode = "";
        _performance.ChangeNote = "Новый драйвер";

        await RunAsync();

        var benchmark = _screen.Session.Benchmark;
        Assert.NotNull(benchmark);
        Assert.Equal(_screen.Config.Current.Thresholds.Comparison, benchmark.Settings);
        var series = benchmark.Series;
        Assert.Equal(FrameCaptureStatus.Completed, series.Status);
        Assert.Equal("game.exe", series.Conditions.Target);
        Assert.Equal(_schedule, series.Schedule);
        Assert.Equal(new BenchmarkNotes("Высокие", "Город", null, "Новый драйвер"), series.Conditions.Notes);
        Assert.Null(series.Conditions.ActiveProfile);
        Assert.All(series.Runs, run => Assert.Equal(FrameMetricsStatus.Ok, run.Metrics.Status));
        Assert.Equal(2, series.Runs.Count);
    }

    [Fact]
    public async Task Start_saves_the_series_and_compares_it_with_the_previous_series_of_the_same_game()
    {
        await _store.SaveAsync(Older("prev", hours: 3), Ct);
        await _store.SaveAsync(Older("other", hours: 1) is var other
            ? other with { Conditions = other.Conditions with { Target = "other.exe" } }
            : null!, Ct);

        await RunAsync();

        var saved = Assert.Single(_store.Series, s => s.Id is not "prev" and not "other");
        Assert.Equal(saved.Id, _performance.SelectedSeries?.Id);
        Assert.Equal([saved.Id, "other", "prev"], _performance.History.Select(h => h.Id));
        var benchmark = _screen.Session.Benchmark;
        Assert.NotNull(benchmark);
        Assert.Equal(saved.Id, benchmark.Series.Id);
        Assert.Equal("prev", benchmark.Comparison?.BaselineId);
        Assert.Equal(saved.Id, Assert.Single(benchmark.Chain).CandidateId);
    }

    [Fact]
    public async Task Series_without_valid_runs_is_shown_but_not_saved()
    {
        _capture.Result = _ => FakeFrameCaptureProvider.Empty;

        await RunAsync();

        Assert.Empty(_store.Series);
        Assert.Empty(_performance.History);
        Assert.Null(_performance.SelectedSeries);
        Assert.Equal(FrameCaptureStatus.NoFrames, _screen.Session.Benchmark?.Series.Status);
        Assert.Null(_screen.Session.Benchmark?.Comparison);
        Assert.NotNull(_performance.StatusMessage);
    }

    [Fact]
    public async Task Save_failure_still_shows_the_series()
    {
        _store.FailSave = true;

        await RunAsync();

        Assert.Equal(FrameCaptureStatus.Completed, _screen.Session.Benchmark?.Series.Status);
        Assert.Empty(_performance.History);
        Assert.Equal(Strings.SeriesSaveFailed, _performance.StatusMessage);
    }

    [Fact]
    public async Task Sensor_failure_does_not_fail_the_series()
    {
        _sensors.FailAlways = true;

        await RunAsync();

        var series = _screen.Session.Benchmark?.Series;
        Assert.NotNull(series);
        Assert.Equal(FrameCaptureStatus.Completed, series.Status);
        Assert.True(series.HasValidRuns);
        Assert.Contains(_recorderLog.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Cues_sound_at_the_start_and_end_of_every_run()
    {
        Configure(runs: 2, seconds: 10, pause: 2, delay: 3);
        _performance.SoundCues = true;

        await RunAsync();

        Assert.Equal(["start", "end", "start", "end"], _cues.Events);
    }

    [Fact]
    public async Task No_cues_when_sound_is_off()
    {
        Configure(runs: 2, seconds: 10, pause: 2, delay: 3);
        _performance.SoundCues = false;

        await RunAsync();

        Assert.Empty(_cues.Events);
    }

    [Fact]
    public async Task Progress_text_counts_down_to_the_end_of_each_phase()
    {
        Configure(runs: 2, seconds: 10, pause: 5, delay: 3);

        var start = _performance.StartCommand.ExecuteAsync(null);
        await WaitForProgress(Format(Strings.ProgressStartDelayFormat, 1, 2, 3));
        _screen.Time.Advance(TimeSpan.FromSeconds(1));
        await WaitForProgress(Format(Strings.ProgressStartDelayFormat, 1, 2, 2));
        _screen.Time.Advance(TimeSpan.FromSeconds(2));
        await WaitForProgress(Format(Strings.ProgressRunFormat, 1, 2, 10));
        _screen.Time.Advance(TimeSpan.FromSeconds(4));
        await WaitForProgress(Format(Strings.ProgressRunFormat, 1, 2, 6));
        _screen.Time.Advance(TimeSpan.FromSeconds(6));
        await WaitForProgress(Format(Strings.ProgressPauseFormat, 2, 5));
        _screen.Time.Advance(TimeSpan.FromSeconds(5));
        await WaitForProgress(Format(Strings.ProgressRunFormat, 2, 2, 10));
        _screen.Time.Advance(TimeSpan.FromSeconds(10));
        await WaitForProgress(Strings.ProgressProcessing);

        _screen.Time.Advance(TimeSpan.FromSeconds(2));
        await start.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Null(_performance.ProgressText);
    }

    [Fact]
    public async Task Cancel_during_a_run_ends_its_cue_and_keeps_nothing_in_history()
    {
        _performance.SoundCues = true;

        var start = _performance.StartCommand.ExecuteAsync(null);
        await ScreenSession.WaitUntilAsync(() => _capture.StartedAt is not null, "capture did not start");
        for (var step = 0; step < 10 && _cues.Events.Count == 0; step++)
        {
            _screen.Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(2, Ct);
        }
        await ScreenSession.WaitUntilAsync(() => _cues.Events.Count == 1, "run did not start");
        _performance.StartCancelCommand.Execute(null);
        await start.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(["start", "end"], _cues.Events);
        Assert.Null(_performance.ProgressText);
        Assert.Equal(FrameCaptureStatus.Cancelled, _screen.Session.Benchmark?.Series.Status);
        Assert.Empty(_store.Series);
    }

    [Fact]
    public async Task Invalid_series_settings_disable_start()
    {
        await _performance.CheckReadinessCommand.ExecuteAsync(null);
        Assert.True(_performance.StartCommand.CanExecute(null));

        _performance.RunCountText = "0";
        Assert.False(_performance.StartCommand.CanExecute(null));
        _performance.RunCountText = "11";
        Assert.False(_performance.StartCommand.CanExecute(null));
        _performance.RunCountText = "3";
        _performance.RunSecondsText = "abc";
        Assert.False(_performance.StartCommand.CanExecute(null));
        _performance.RunSecondsText = "9";
        Assert.False(_performance.StartCommand.CanExecute(null));
        _performance.RunSecondsText = "10";
        _performance.PauseSecondsText = "301";
        Assert.False(_performance.StartCommand.CanExecute(null));
        _performance.PauseSecondsText = "0";
        _performance.StartDelayText = "2";
        Assert.False(_performance.StartCommand.CanExecute(null));
        _performance.StartDelayText = "3";
        Assert.True(_performance.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task Selecting_a_history_item_rebuilds_the_report_against_the_chosen_baseline()
    {
        await _store.SaveAsync(Older("a", hours: 3), Ct);
        await _store.SaveAsync(Older("b", hours: 2), Ct);
        await _store.SaveAsync(Older("c", hours: 1), Ct);
        await _performance.RefreshHistoryCommand.ExecuteAsync(null);
        Assert.Equal(["c", "b", "a"], _performance.History.Select(h => h.Id));

        _performance.SelectedSeries = _performance.History[2];
        _performance.SetBaselineCommand.Execute(null);
        Assert.Equal("a", _performance.Baseline?.Id);

        Assert.Equal("a", _screen.Session.Benchmark?.Series.Id);
        Assert.Null(_screen.Session.Benchmark?.Comparison);
        Assert.Empty(_screen.Session.Benchmark!.Chain);

        _performance.SelectedSeries = _performance.History[0];
        var benchmark = _screen.Session.Benchmark;
        Assert.Equal("c", benchmark.Series.Id);
        Assert.Equal("a", benchmark.Comparison?.BaselineId);
        Assert.Equal(["b", "c"], benchmark.Chain.Select(c => c.CandidateId));
    }

    [Fact]
    public async Task Without_a_chosen_baseline_the_previous_series_is_the_baseline()
    {
        await _store.SaveAsync(Older("a", hours: 3), Ct);
        await _store.SaveAsync(Older("b", hours: 2), Ct);
        await _store.SaveAsync(Older("c", hours: 1), Ct);
        await _performance.RefreshHistoryCommand.ExecuteAsync(null);

        _performance.SelectedSeries = _performance.History[1];

        Assert.Equal("b", _screen.Session.Benchmark?.Series.Id);
        Assert.Equal("a", _screen.Session.Benchmark?.Comparison?.BaselineId);
        Assert.Equal(["b"], _screen.Session.Benchmark!.Chain.Select(c => c.CandidateId));
    }

    [Fact]
    public async Task Delete_asks_inline_and_removes_the_series_from_store_and_history()
    {
        await _store.SaveAsync(Older("a", hours: 2), Ct);
        await _store.SaveAsync(Older("b", hours: 1), Ct);
        await _performance.RefreshHistoryCommand.ExecuteAsync(null);
        _performance.SelectedSeries = _performance.History.Single(h => h.Id == "a");
        _performance.SetBaselineCommand.Execute(null);

        _performance.AskDeleteCommand.Execute(null);
        Assert.True(_performance.IsConfirmingDelete);
        _performance.CancelDeleteCommand.Execute(null);
        Assert.False(_performance.IsConfirmingDelete);
        Assert.Equal(2, _store.Series.Count);

        _performance.AskDeleteCommand.Execute(null);
        await _performance.DeleteSeriesCommand.ExecuteAsync(null);

        Assert.False(_performance.IsConfirmingDelete);
        Assert.Equal(["b"], _store.Series.Select(s => s.Id));
        Assert.Equal(["b"], _performance.History.Select(h => h.Id));
        Assert.Null(_performance.SelectedSeries);
        Assert.Null(_performance.Baseline);
    }

    [Fact]
    public async Task Running_profile_session_is_noted_in_the_series_conditions()
    {
        _gaming.Processes.Start("cs2", 1234);
        await _gaming.Coordinator.StartAsync(Ct);
        await _gaming.Coordinator.ArmAsync("competitive", new HashSet<string>(StringComparer.Ordinal), Ct);
        await ScreenSession.WaitUntilAsync(() => _gaming.Coordinator.Status.Mode == CoordinatorMode.InSession, "session did not start");

        await RunAsync();

        Assert.Equal("Competitive Gaming", _screen.Session.Benchmark?.Series.Conditions.ActiveProfile);
    }

    [Fact]
    public void History_item_shows_median_metrics_and_dashes_for_missing_values()
    {
        var full = BenchmarkHistoryItem.From(SampleSeries.Full("a"), CultureInfo.InvariantCulture);
        Assert.Equal("game.exe", full.Target);
        Assert.Equal("Новый драйвер", full.Change);
        Assert.Equal("120.0", full.AverageFps);
        Assert.Equal("120.0", full.OnePercentLow);

        var empty = BenchmarkHistoryItem.From(SampleSeries.Full("b") is var b
            ? b with { Summary = null, Conditions = b.Conditions with { Notes = new BenchmarkNotes(null, null, null, null) } }
            : null!, CultureInfo.InvariantCulture);
        Assert.Equal(Strings.NoValue, empty.Change);
        Assert.Equal(Strings.NoValue, empty.AverageFps);
        Assert.Equal(Strings.NoValue, empty.OnePercentLow);
    }

    [Fact]
    public async Task View_loads_and_lays_out_with_history_and_delete_confirmation()
    {
        await _store.SaveAsync(Older("a", hours: 1), Ct);
        await _performance.RefreshHistoryCommand.ExecuteAsync(null);
        _performance.SelectedSeries = _performance.History[0];
        _performance.AskDeleteCommand.Execute(null);

        Assert.Null(WpfHost.TryRender(() => new Booster.App.Views.PerformanceView { DataContext = _performance }));
    }

    public async ValueTask DisposeAsync()
    {
        await _gaming.DisposeAsync();
        _screen.Dispose();
    }

    private static string Text(int value) => value.ToString(CultureInfo.CurrentCulture);

    private static FakeGaming GamingWithOneAction()
    {
        var state = new FakeSystemState { ["a"] = "orig" };
        var profile = ProfileFactory.Profile(games: ["cs2"], actions: [new ProfileActionEntry("a", ActionMode.Allowed)]);
        return new FakeGaming(new FakeProfileRepository(profile), new FakeActionCatalog().Add(new FakeAction(state, "a")),
            [new FakeRestorer(state, "a")]);
    }

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private static BenchmarkSeries Older(string id, int hours) =>
        SampleSeries.Full(id) with { StartedAt = SessionFactory.Start.AddHours(-hours) };

    private void Configure(int runs, int seconds, int pause, int delay)
    {
        _schedule = new BenchmarkSchedule(runs, TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(pause), TimeSpan.FromSeconds(delay));
        _performance.RunCountText = Text(runs);
        _performance.RunSecondsText = Text(seconds);
        _performance.PauseSecondsText = Text(pause);
        _performance.StartDelayText = Text(delay);
    }

    private Task WaitForProgress(string expected) =>
        ScreenSession.WaitUntilAsync(() => _performance.ProgressText == expected, $"progress is «{_performance.ProgressText}», expected «{expected}»");

    private async Task RunAsync()
    {
        var start = _performance.StartCommand.ExecuteAsync(null);
        await ScreenSession.WaitUntilAsync(() => _capture.StartedAt is not null || start.IsCompleted, "capture did not start");
        for (var step = 0; step < 200 && !start.IsCompleted; step++)
        {
            _screen.Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(2, Ct);
        }

        await start.WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    private FrameCaptureResult FramesInRuns(long startTimestamp)
    {
        var frames = _schedule.Windows(startTimestamp, _screen.Time.TimestampFrequency)
            .SelectMany(w => Enumerable.Range(0, 601).Select(k => new FrameSample(w.StartQpc + k * ((w.EndQpc - w.StartQpc) / 601), 10.0, null, null, null)))
            .ToArray();
        return new FrameCaptureResult(FrameCaptureStatus.Completed,
            new FrameCapture("game.exe", 42, frames, "MsBetweenPresents", 0, ["Application"], [], "capture.csv"),
            Reading<AdapterLuid>.Unknown(), null);
    }

    private sealed class NoDisplays : IInventoryDetector<Reading<IReadOnlyList<DisplayInfo>>>
    {
        public string SectionId => "displays";

        public Task<Reading<IReadOnlyList<DisplayInfo>>> DetectAsync(CancellationToken ct) =>
            Task.FromResult(Reading<IReadOnlyList<DisplayInfo>>.Unknown("test"));
    }

    private sealed class NoWindows : IProcessListSource
    {
        public IReadOnlyList<ProcessEntry> GetWindowedProcesses() => [];
    }
}
