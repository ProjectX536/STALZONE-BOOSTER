using Booster.Core.Abstractions;
using Booster.Core.Metrics;

namespace Booster.Core.Benchmark;

public enum BottleneckClass
{
    CpuLimited,
    GpuLimited,
    VideoMemoryPressure,
    MemoryPressure,
    ThermalThrottling,
    Storage,
    DriverLatency,
    FramePacing,
    ExternalLimit,
}

public enum AnalysisConfidence
{
    Low,
    Medium,
    High,
}

public enum EvidenceKind
{
    GpuBusyFrameShare,
    GpuUtilization,
    BusiestThread,
    SystemCpu,
    CpuBusyFrameShare,
    FpsAtRefreshRate,
    SyncedFrames,
    UniformFrameTimes,
    VramUsage,
    AvailableMemory,
    CommitCharge,
    PageReads,
    DiskBusy,
    DiskLatency,
    Throttle,
    StutterRate,
    PercentileRatio,
    DpcTime,
    InterruptTime,
}

public enum GpuLoadState
{
    Unknown,
    Saturated,
    Underloaded,
    Normal,
}

public sealed record Evidence(EvidenceKind Kind, Reading<double> Value, double? Threshold, bool Supports, string? Detail = null);

public sealed record BottleneckFinding(BottleneckClass Class, AnalysisConfidence Confidence, IReadOnlyList<Evidence> Evidence);

public sealed record BottleneckAssessment(IReadOnlyList<BottleneckFinding> Findings, GpuLoadState GpuLoad, IReadOnlyList<EvidenceKind> MissingData);

public sealed record BottleneckInput(FrameMetrics Metrics, FrameStats Frames, StutterResult Stutter, Reading<double> RefreshRateHz,
    SeriesTelemetry Telemetry);
