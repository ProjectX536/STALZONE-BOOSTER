namespace Booster.Core.Metrics;

public enum FrameMetricsStatus
{
    Ok,
    InsufficientData,
}

/// <summary>Метрики одного прогона. Определения — docs/ARCHITECTURE.md, раздел 10.</summary>
public sealed record FrameMetrics(
    int FrameCount,
    double DurationMs,
    double AverageFps,
    double OnePercentLowFps,
    double FrameTimeP50Ms,
    double FrameTimeP95Ms,
    double FrameTimeP99Ms);

/// <summary><see cref="Metrics"/> заполнен только при статусе <see cref="FrameMetricsStatus.Ok"/>.</summary>
public sealed record FrameMetricsResult(FrameMetricsStatus Status, FrameMetrics? Metrics, int ValidIntervals, int InvalidIntervals);
