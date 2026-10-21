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

/// <summary>Событие журнала Windows, говорящее о нестабильности.</summary>
/// <param name="Subject">Сбойное приложение, код BugCheck, устройство — что есть в записи; иначе null.</param>
public sealed record StabilityEvent(StabilityEventKind Kind, DateTimeOffset Time, string Provider, int EventId, string? Subject);

/// <param name="Events">От новых к старым.</param>
/// <param name="Truncated">Событий больше предела: прочитаны самые новые.</param>
public sealed record StabilityLog(DateTimeOffset Since, IReadOnlyList<StabilityEvent> Events, bool Truncated);

/// <summary>События стабильности из журналов System и Application. Только чтение.</summary>
public interface IStabilityEventSource
{
    Task<StabilityLog> ReadAsync(DateTimeOffset since, int maxEvents, CancellationToken ct);
}
