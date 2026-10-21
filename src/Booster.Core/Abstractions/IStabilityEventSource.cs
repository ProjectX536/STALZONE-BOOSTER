namespace Booster.Core.Abstractions;

public enum StabilityEventKind
{
    UnexpectedShutdown,
    BugCheck,
    DisplayDriverReset,
    HardwareError,
    DiskError,
    FileSystemError,
    ApplicationCrash,
    ApplicationHang,
}

public sealed record StabilityEvent(StabilityEventKind Kind, DateTimeOffset Time, string Provider, int EventId, string? Subject);

public sealed record StabilityLog(DateTimeOffset Since, IReadOnlyList<StabilityEvent> Events, bool Truncated);

public interface IStabilityEventSource
{
    Task<StabilityLog> ReadAsync(DateTimeOffset since, int maxEvents, CancellationToken ct);
}
