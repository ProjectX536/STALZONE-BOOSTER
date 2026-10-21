using Booster.Core.Abstractions;
using Booster.Core.Metrics;

namespace Booster.Core.Benchmark;

/// <summary>Что ограничивает FPS. Порядок — порядок находок при равной уверенности.</summary>
public enum BottleneckClass
{
    CpuLimited,
    GpuLimited,
    VideoMemoryPressure,
    MemoryPressure,
    ThermalThrottling,
    Storage,
    /// <summary>Драйверы долго держат процессор в DPC и прерываниях. Причину показывает только трасса WPR.</summary>
    DriverLatency,
    FramePacing,
    /// <summary>Vsync, ограничитель FPS игры или драйвера: кадры ждут не железо.</summary>
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
    /// <summary>Нет ни доли занятости GPU в кадре, ни загрузки GPU.</summary>
    Unknown,
    Saturated,
    Underloaded,
    Normal,
}

/// <param name="Threshold">Порог, с которым сравнивалось значение; null — сравнения нет.</param>
/// <param name="Supports">true — сигнал за вывод, false — против (показывается, чтобы было видно, почему уверенность ниже).</param>
public sealed record Evidence(EvidenceKind Kind, Reading<double> Value, double? Threshold, bool Supports, string? Detail = null);

public sealed record BottleneckFinding(BottleneckClass Class, AnalysisConfidence Confidence, IReadOnlyList<Evidence> Evidence);

/// <param name="MissingData">Сигналы, которых не было в серии, в порядке <see cref="EvidenceKind"/>.</param>
public sealed record BottleneckAssessment(IReadOnlyList<BottleneckFinding> Findings, GpuLoadState GpuLoad, IReadOnlyList<EvidenceKind> MissingData);

public sealed record BottleneckInput(FrameMetrics Metrics, FrameStats Frames, StutterResult Stutter, Reading<double> RefreshRateHz,
    SeriesTelemetry Telemetry);
