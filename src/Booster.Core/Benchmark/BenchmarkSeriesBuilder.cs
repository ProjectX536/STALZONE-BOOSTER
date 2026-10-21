using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Inventory;
using Booster.Core.Metrics;

namespace Booster.Core.Benchmark;

/// <summary>Серия из захвата: условия теста, прогоны по окнам, сводка по годным прогонам, анализ и рекомендации.</summary>
public static class BenchmarkSeriesBuilder
{
    /// <summary>Сколько фоновых процессов попадает в условия теста.</summary>
    public const int BackgroundCount = 5;

    public const string DisplaySource = "Display configuration";
    public const string DisplayModeNote = "display mode at the start of the first run, not the game's render resolution";
    public const string PrimaryDisplayNote = "no display on the render adapter; the primary display is shown";
    public const string NoDisplayNote = "no active display";
    public const string DisplayModeUnknownNote = "display resolution or refresh rate is unknown";

    public static BenchmarkSeries Build(BenchmarkCaptureData data, Thresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(thresholds);

        var request = data.Request;
        var result = data.Capture;
        var capture = result is { Status: FrameCaptureStatus.Completed } ? result.Capture : null;
        var telemetry = TelemetryAggregator.Aggregate(data.Samples, data.Windows, result.RenderAdapter, thresholds, BackgroundCount);
        var display = SelectDisplay(data.Displays, result.RenderAdapter);
        var target = capture?.ProcessName
            ?? request.ProcessName
            ?? (request.ProcessId is { } pid ? "PID " + pid.ToString(CultureInfo.InvariantCulture) : string.Empty);
        var conditions = new BenchmarkConditions(target, telemetry.RenderAdapterName, telemetry.DriverVersion, display, data.PowerPlan,
            data.PowerSource, request.ActiveProfile, request.Notes, capture?.PresentModes ?? [], telemetry.Background);

        if (capture is null)
            return new BenchmarkSeries(data.Id, data.StartedAt, request.Schedule, result.Status, result.Detail, conditions, [], null, telemetry,
                null, [], null);

        var slices = RunSlicer.Slice(capture.Frames, data.Windows);
        var runs = slices.Select((frames, index) => Run(index + 1, frames, thresholds)).ToArray();
        var validFrames = slices.Where((_, index) => runs[index].Metrics.Status == FrameMetricsStatus.Ok).ToArray();

        var summary = Summarize(runs);
        var bottleneck = validFrames.Length == 0 ? null : Analyze(validFrames, display, telemetry, thresholds);
        var vrr = display is { IsAvailable: true, Value.VrrEnabled: { } enabled }
            ? Reading<bool>.Known(enabled, display.Source ?? DisplaySource)
            : Reading<bool>.Unknown(display.Source ?? DisplaySource);
        var recommendations = RecommendationEngine.Recommend(new RecommendationInput(bottleneck, summary, data.PowerSource, vrr, telemetry),
            thresholds.Comparison, thresholds.Recommendations);

        return new BenchmarkSeries(data.Id, data.StartedAt, request.Schedule, result.Status, result.Detail, conditions, runs, summary, telemetry,
            bottleneck, recommendations, capture.CsvPath);
    }

    private static BenchmarkRun Run(int number, IReadOnlyList<FrameSample> frames, Thresholds thresholds)
    {
        var intervals = Intervals(frames);
        var metrics = FrameMetricsCalculator.Calculate(intervals, thresholds.MinimumFramesForMetrics);
        var stutter = metrics.Status == FrameMetricsStatus.Ok ? StutterAnalyzer.Analyze(intervals, thresholds.Stutter) : null;
        return new BenchmarkRun(number, metrics, stutter, FrameStatsCalculator.Compute(frames));
    }

    /// <summary>По прогонам со статусом Ok; годных нет — null.</summary>
    private static BenchmarkSummary? Summarize(IReadOnlyList<BenchmarkRun> runs)
    {
        var valid = runs.Where(run => run.Metrics is { Status: FrameMetricsStatus.Ok, Metrics: not null }).ToArray();
        if (valid.Length == 0)
            return null;

        MetricSpread Spread(Func<BenchmarkRun, double> value)
        {
            var values = valid.Select(value).ToArray();
            return new MetricSpread(Median.Of(values.ToArray()), values.Min(), values.Max());
        }

        var fps = Spread(run => run.Metrics.Metrics!.AverageFps);
        return new BenchmarkSummary(
            valid.Length,
            fps,
            Spread(run => run.Metrics.Metrics!.OnePercentLowFps),
            Spread(run => run.Metrics.Metrics!.FrameTimeP95Ms),
            Spread(run => run.Metrics.Metrics!.FrameTimeP99Ms),
            Spread(run => run.Stutter?.StuttersPerMinute ?? 0),
            fps.Median > 0 ? (fps.Max - fps.Min) / fps.Median * 100 : 0);
    }

    /// <summary>Анализ — по объединению кадров годных прогонов: одна оценка на серию, а не на каждый прогон.</summary>
    private static BottleneckAssessment? Analyze(IReadOnlyList<IReadOnlyList<FrameSample>> validFrames, Reading<DisplayMode> display,
        SeriesTelemetry telemetry, Thresholds thresholds)
    {
        FrameSample[] frames = [.. validFrames.SelectMany(run => run)];
        var intervals = Intervals(frames);
        if (FrameMetricsCalculator.Calculate(intervals, thresholds.MinimumFramesForMetrics) is not { Metrics: { } metrics })
            return null;

        var refresh = display is { IsAvailable: true, Value: { } mode }
            ? Reading<double>.Known(mode.RefreshRateHz, display.Source ?? DisplaySource)
            : new Reading<double>(0, display.Status, display.Source, display.Note);
        var input = new BottleneckInput(metrics, FrameStatsCalculator.Compute(frames), StutterAnalyzer.Analyze(intervals, thresholds.Stutter),
            refresh, telemetry);
        return BottleneckAnalyzer.Analyze(input, thresholds.Bottleneck);
    }

    /// <summary>
    /// Дисплей адаптера рендеринга (из нескольких — основной); не найден — основной с примечанием.
    /// Режим — на начало первого прогона, а не разрешение рендеринга игры.
    /// </summary>
    private static Reading<DisplayMode> SelectDisplay(Reading<IReadOnlyList<DisplayInfo>> displays, Reading<AdapterLuid> renderAdapter)
    {
        var source = displays.Source ?? DisplaySource;
        if (!displays.IsAvailable || displays.Value is not { } list)
            return new Reading<DisplayMode>(null, displays.Status, displays.Source, displays.Note);

        var onAdapter = renderAdapter.IsAvailable
            ? list.Where(d => d.AdapterLuid.IsAvailable && d.AdapterLuid.Value == renderAdapter.Value).ToArray()
            : [];
        var chosen = onAdapter.FirstOrDefault(d => d.IsPrimary) ?? onAdapter.FirstOrDefault();
        var note = DisplayModeNote;
        if (chosen is null)
        {
            chosen = list.FirstOrDefault(d => d.IsPrimary) ?? list.FirstOrDefault();
            note = PrimaryDisplayNote + "; " + DisplayModeNote;
        }

        if (chosen is null)
            return Reading<DisplayMode>.Unknown(source, NoDisplayNote);
        if (!chosen.Width.IsAvailable || !chosen.Height.IsAvailable || !chosen.RefreshRateHz.IsAvailable)
            return Reading<DisplayMode>.Unknown(source, DisplayModeUnknownNote);

        var mode = new DisplayMode(chosen.MonitorName.IsAvailable ? chosen.MonitorName.Value : null, chosen.Width.Value, chosen.Height.Value,
            chosen.RefreshRateHz.Value, chosen.HdrEnabled.IsAvailable ? chosen.HdrEnabled.Value : null,
            chosen.VrrEnabled.IsAvailable ? chosen.VrrEnabled.Value : null);
        return new Reading<DisplayMode>(mode, DataStatus.Available, source, note);
    }

    private static double[] Intervals(IReadOnlyList<FrameSample> frames) => [.. frames.Select(frame => frame.IntervalMs)];
}
