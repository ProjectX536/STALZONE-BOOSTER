using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Core.Metrics;

namespace Booster.Core.Benchmark;

/// <summary>Пометки пользователя к серии: что за сцена и что поменяли перед ней. Booster их не проверяет.</summary>
public sealed record BenchmarkNotes(string? GraphicsProfile, string? Scene, string? WindowMode, string? Change);

public sealed record BenchmarkRequest(string? ProcessName, int? ProcessId, BenchmarkSchedule Schedule, BenchmarkNotes Notes, string? ActiveProfile);

public sealed record DisplayMode(string? MonitorName, int Width, int Height, double RefreshRateHz, bool? HdrEnabled, bool? VrrEnabled);

public sealed record BenchmarkConditions(
    string Target,
    Reading<string> RenderAdapter,
    Reading<string> DriverVersion,
    Reading<DisplayMode> Display,
    Reading<string> PowerPlan,
    PowerSource PowerSource,
    string? ActiveProfile,
    BenchmarkNotes Notes,
    IReadOnlyList<string> PresentModes,
    IReadOnlyList<BackgroundProcess> Background);

/// <param name="Number">С 1.</param>
public sealed record BenchmarkRun(int Number, FrameMetricsResult Metrics, StutterResult? Stutter, FrameStats Frames);

public sealed record MetricSpread(double Median, double Min, double Max);

/// <summary>По прогонам со статусом Ok. AverageFpsVariationPercent = (max − min) / медиана × 100.</summary>
public sealed record BenchmarkSummary(int ValidRuns, MetricSpread AverageFps, MetricSpread OnePercentLowFps,
    MetricSpread FrameTimeP95Ms, MetricSpread FrameTimeP99Ms, MetricSpread StuttersPerMinute, double AverageFpsVariationPercent);

public sealed record BenchmarkSeries(
    string Id,
    DateTimeOffset StartedAt,
    BenchmarkSchedule Schedule,
    FrameCaptureStatus Status,
    string? Detail,
    BenchmarkConditions Conditions,
    IReadOnlyList<BenchmarkRun> Runs,
    BenchmarkSummary? Summary,
    SeriesTelemetry Telemetry,
    BottleneckAssessment? Bottleneck,
    IReadOnlyList<Recommendation> Recommendations,
    string? CsvPath)
{
    public bool HasValidRuns => Summary is { ValidRuns: > 0 };
}

public sealed record BenchmarkCaptureData(string Id, BenchmarkRequest Request, DateTimeOffset StartedAt, FrameCaptureResult Capture,
    IReadOnlyList<RunWindow> Windows, IReadOnlyList<BenchmarkSample> Samples, Reading<IReadOnlyList<DisplayInfo>> Displays,
    Reading<string> PowerPlan, PowerSource PowerSource);
