namespace Booster.Core.Configuration;

public sealed record Thresholds(
    int SchemaVersion,
    int MinimumFramesForMetrics,
    double LowFreeSpacePercent,
    ThrottleSettings Throttle,
    StutterSettings Stutter,
    BottleneckSettings Bottleneck,
    ComparisonSettings Comparison,
    RecommendationSettings Recommendations,
    MemoryThresholds Memory,
    NetworkThresholds Network,
    StartupThresholds Startup,
    GameNetworkThresholds GameNetwork)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record ThrottleSettings(
    double TemperatureMarginC,
    double GpuBusyUtilizationPercent,
    double ClockDropPercent,
    int MinimumSamples,
    int SessionWindowSamples);

public sealed record StutterSettings(double Factor, double MinimumExcessMs, int WindowFrames);

public sealed record BottleneckSettings(
    double GpuBoundFrameShare,
    double GpuBusyPercent,
    double GpuIdleFrameShare,
    double GpuIdlePercent,
    double BusyThreadPercent,
    double SystemCpuPercent,
    double CpuBoundFrameShare,
    double RefreshTolerancePercent,
    double SyncedFrameShare,
    double UniformFrameTimeRatio,
    double VramUsedPercent,
    double AvailableMemoryPercent,
    double CommitPercent,
    double PageReadsPerSecond,
    double DiskBusyPercent,
    double DiskLatencyMs,
    double StuttersPerMinute,
    double PercentileRatio,
    double DpcTimePercent,
    double InterruptTimePercent);

public sealed record ComparisonSettings(int MinimumRuns, double MinimumChangePercent, double RunVariationPercent,
    double MinimumStutterChangePerMinute);

public sealed record RecommendationSettings(double BackgroundCpuPercent);

public sealed record MemoryThresholds(double CommitPercent);

public sealed record NetworkThresholds(double GatewayLatencyMs, double HostLatencyMs, double JitterMs, double LossPercent);

public sealed record StartupThresholds(double MinPrivateMemoryMb);

public sealed record GameNetworkThresholds(double MinGameEventsPerSecond, double SpikeMinDeltaMs, double SpikeMadFactor,
    int PeriodicMinSpikes, double PeriodicMaxVariation, double RouteJumpMs, IReadOnlyList<double> BufferbloatGradesMs,
    double LoadMinThroughputMbps, double BackgroundTrafficKBps, int WifiWeakSignalDbm,
    int ComparisonMinimumRuns, double ComparisonMinimumChangeMs, double ComparisonMinimumChangePercent);
