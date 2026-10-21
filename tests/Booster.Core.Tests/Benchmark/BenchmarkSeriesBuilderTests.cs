using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Inventory;
using Booster.Core.Metrics;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.Benchmark;

public class BenchmarkSeriesBuilderTests
{
    private const string Source = "test";
    private const long WindowTicks = 100_000;

    private static readonly AdapterLuid RenderLuid = new(7, 0);
    private static readonly AdapterLuid OtherLuid = new(3, 0);
    private static readonly BenchmarkSchedule Schedule = new(3, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3));
    private static readonly BenchmarkNotes Notes = new("High", "Деревня новичков", "Fullscreen", "до изменений");

    private static RunWindow[] Windows(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new RunWindow(i, i * 2 * WindowTicks, i * 2 * WindowTicks + WindowTicks))];

    /// <summary>601 кадр с 16,7 мс в окне: первый отбрасывается, остаётся 600.</summary>
    private static IEnumerable<FrameSample> FramesIn(RunWindow window) =>
        Enumerable.Range(0, 601).Select(k => new FrameSample(window.StartQpc + k * 100, 16.7, 8, 12, 0));

    private static FrameCaptureResult Completed(IEnumerable<FrameSample> frames, Reading<AdapterLuid>? renderAdapter = null) =>
        new(FrameCaptureStatus.Completed,
            new FrameCapture("game.exe", 42, [.. frames], "MsBetweenPresents", 0, ["Application"], ["Hardware: Independent Flip"], @"C:\capture.csv"),
            renderAdapter ?? Reading<AdapterLuid>.Known(RenderLuid, Source),
            null);

    private static DisplayInfo Display(string name, double hz, AdapterLuid luid, bool primary, bool vrr = true) =>
        new(Reading<string>.Known(name, Source), Reading<int>.Known(2560, Source), Reading<int>.Known(1440, Source),
            Reading<double>.Known(hz, Source), Reading<bool>.Known(true, Source), Reading<bool>.Known(false, Source),
            Reading<bool>.Known(vrr, Source), Reading<AdapterLuid>.Known(luid, Source), primary);

    private static readonly IReadOnlyList<DisplayInfo> TwoDisplays =
        [Display("Primary", 60, OtherLuid, primary: true), Display("Gaming", 144, RenderLuid, primary: false)];

    private static BenchmarkCaptureData Data(FrameCaptureResult capture, RunWindow[]? windows = null, IReadOnlyList<DisplayInfo>? displays = null) =>
        new("20261020-120000-game.exe", new BenchmarkRequest("game.exe", null, Schedule, Notes, "Competitive"),
            new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.FromHours(3)), capture, windows ?? Windows(3), [],
            Reading<IReadOnlyList<DisplayInfo>>.Known(displays ?? TwoDisplays, Source), Reading<string>.Known("Balanced", Source),
            PowerSource.AcPower);

    private static BenchmarkSeries Build(BenchmarkCaptureData data) => BenchmarkSeriesBuilder.Build(data, FakeConfiguration.DefaultThresholds);

    [Fact]
    public void Completed_capture_gives_runs_summary_analysis_and_recommendations()
    {
        var windows = Windows(3);

        var series = Build(Data(Completed(windows.SelectMany(FramesIn)), windows));

        Assert.Equal(FrameCaptureStatus.Completed, series.Status);
        Assert.Equal([1, 2, 3], series.Runs.Select(r => r.Number));
        Assert.All(series.Runs, run =>
        {
            Assert.Equal(FrameMetricsStatus.Ok, run.Metrics.Status);
            Assert.NotNull(run.Stutter);
            Assert.Equal(600, run.Frames.Frames);
        });
        var summary = Assert.IsType<BenchmarkSummary>(series.Summary);
        Assert.Equal(3, summary.ValidRuns);
        Assert.Equal(59.9, summary.AverageFps.Median, 1);
        Assert.Equal(0, summary.AverageFpsVariationPercent, 6);
        Assert.True(series.HasValidRuns);
        Assert.NotNull(series.Bottleneck);
        Assert.NotEmpty(series.Recommendations);
        Assert.Equal(@"C:\capture.csv", series.CsvPath);
    }

    [Fact]
    public void Run_without_frames_is_insufficient_and_summary_uses_valid_runs()
    {
        var windows = Windows(3);
        var frames = FramesIn(windows[0]).Concat(FramesIn(windows[2]));

        var series = Build(Data(Completed(frames), windows));

        Assert.Equal(FrameMetricsStatus.InsufficientData, series.Runs[1].Metrics.Status);
        Assert.Null(series.Runs[1].Stutter);
        Assert.Equal(2, series.Summary!.ValidRuns);
        Assert.NotNull(series.Bottleneck);
    }

    [Fact]
    public void No_valid_runs_give_no_summary_or_analysis_but_ask_for_more_data()
    {
        var series = Build(Data(Completed([])));

        Assert.Equal(3, series.Runs.Count);
        Assert.Null(series.Summary);
        Assert.False(series.HasValidRuns);
        Assert.Null(series.Bottleneck);
        Assert.Equal([RecommendationId.NeedMoreData], series.Recommendations.Select(r => r.Id));
    }

    [Fact]
    public void Failed_capture_gives_status_only()
    {
        var capture = new FrameCaptureResult(FrameCaptureStatus.ElevationDeclined, null, Reading<AdapterLuid>.Unknown(Source), "UAC declined");

        var series = Build(Data(capture));

        Assert.Equal(FrameCaptureStatus.ElevationDeclined, series.Status);
        Assert.Equal("UAC declined", series.Detail);
        Assert.Empty(series.Runs);
        Assert.Null(series.Summary);
        Assert.Null(series.Bottleneck);
        Assert.Empty(series.Recommendations);
        Assert.Null(series.CsvPath);
        Assert.Equal("game.exe", series.Conditions.Target);
    }

    [Fact]
    public void Display_of_render_adapter_preferred_over_primary()
    {
        var display = Build(Data(Completed([]))).Conditions.Display;

        Assert.True(display.IsAvailable);
        Assert.Equal("Gaming", display.Value!.MonitorName);
        Assert.Equal(144, display.Value.RefreshRateHz);
        Assert.Equal(BenchmarkSeriesBuilder.DisplayModeNote, display.Note);
    }

    [Fact]
    public void Unknown_render_adapter_uses_primary_display_with_note()
    {
        var display = Build(Data(Completed([], Reading<AdapterLuid>.Unknown(Source)))).Conditions.Display;

        Assert.Equal("Primary", display.Value!.MonitorName);
        Assert.Contains(BenchmarkSeriesBuilder.PrimaryDisplayNote, display.Note);
        Assert.Contains(BenchmarkSeriesBuilder.DisplayModeNote, display.Note);
    }

    [Fact]
    public void Display_refresh_rate_drives_external_limit_analysis()
    {
        var windows = Windows(3);
        var frames = windows.SelectMany(w => FramesIn(w).Select(f => f with { IntervalMs = 1000.0 / 144, SyncInterval = 1 }));

        var series = Build(Data(Completed(frames), windows));

        Assert.Contains(series.Bottleneck!.Findings, f => f is { Class: BottleneckClass.ExternalLimit, Confidence: AnalysisConfidence.High });
    }

    [Fact]
    public void Unknown_displays_give_unknown_display_and_missing_refresh()
    {
        var windows = Windows(3);
        var data = Data(Completed(windows.SelectMany(FramesIn)), windows) with
        {
            Displays = Reading<IReadOnlyList<DisplayInfo>>.Failure(Source, "query failed"),
        };

        var series = Build(data);

        Assert.Equal(DataStatus.Failed, series.Conditions.Display.Status);
        Assert.Contains(EvidenceKind.FpsAtRefreshRate, series.Bottleneck!.MissingData);
    }

    [Fact]
    public void Present_modes_and_notes_recorded_in_conditions()
    {
        var conditions = Build(Data(Completed([]))).Conditions;

        Assert.Equal("game.exe", conditions.Target);
        Assert.Equal(["Hardware: Independent Flip"], conditions.PresentModes);
        Assert.Equal(Notes, conditions.Notes);
        Assert.Equal("Competitive", conditions.ActiveProfile);
        Assert.Equal("Balanced", conditions.PowerPlan.Value);
        Assert.Equal(PowerSource.AcPower, conditions.PowerSource);
    }
}
