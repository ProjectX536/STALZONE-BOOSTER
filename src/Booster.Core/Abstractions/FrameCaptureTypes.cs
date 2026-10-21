using Booster.Core.Inventory;

namespace Booster.Core.Abstractions;

public sealed record FrameCaptureRequest(string? ProcessName, int? ProcessId, TimeSpan Duration)
{
    public bool StopWhenProcessExits { get; init; }

    public FrameCapturePurpose Purpose { get; init; }
}

public enum FrameCapturePurpose
{
    Benchmark,
    GameSession,
}

public enum FrameCaptureStatus
{
    Completed,
    NotInstalled,
    ElevationDeclined,
    TargetNotFound,
    NoFrames,
    UnsupportedFormat,
    Failed,
    Cancelled,
}

public enum FrameCaptureMode
{
    Direct,
    ElevationPrompt,
}

public sealed record FrameCaptureAvailability(bool IsInstalled, string? ExecutablePath, string? Version, FrameCaptureMode Mode);

public readonly record struct FrameSample(long? PresentQpc, double IntervalMs, double? CpuBusyMs, double? GpuBusyMs, int? SyncInterval);

public sealed record FrameCapture(
    string ProcessName,
    int ProcessId,
    IReadOnlyList<FrameSample> Frames,
    string IntervalColumn,
    int DiscardedRows,
    IReadOnlyList<string> FrameTypes,
    IReadOnlyList<string> PresentModes,
    string CsvPath)
{
    public IReadOnlyList<double> IntervalsMs() => Frames.Select(f => f.IntervalMs).ToArray();
}

public sealed record FrameCaptureResult(
    FrameCaptureStatus Status,
    FrameCapture? Capture,
    Reading<AdapterLuid> RenderAdapter,
    string? Detail);

public interface IFrameCaptureProvider
{
    Task<FrameCaptureAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken);

    Task<FrameCaptureResult> CaptureAsync(FrameCaptureRequest request, Action? started, CancellationToken cancellationToken);
}
