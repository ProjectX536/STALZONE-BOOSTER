namespace Booster.Core.Metrics;

public enum FrameMetricsStatus
{
    Ok,
    InsufficientData,
}

public sealed record FrameMetrics(
    int FrameCount,
    double DurationMs,
    double AverageFps,
    double OnePercentLowFps,
    double FrameTimeP50Ms,
    double FrameTimeP95Ms,
    double FrameTimeP99Ms);

public sealed record FrameMetricsResult(FrameMetricsStatus Status, FrameMetrics? Metrics, int ValidIntervals, int InvalidIntervals);
