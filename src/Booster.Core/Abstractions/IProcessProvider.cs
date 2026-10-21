namespace Booster.Core.Abstractions;

/// <param name="Name">Имя exe без «.exe».</param>
public sealed record ProcessInfo(int ProcessId, string Name);

/// <summary>PID переиспользуется — процесс узнаётся по паре PID и времени старта.</summary>
public readonly record struct ProcessIdentity(int ProcessId, DateTimeOffset StartTime);

/// <summary>Запущенные процессы; только чтение.</summary>
public interface IProcessProvider
{
    /// <summary>Все процессы; сами процессы не открываются.</summary>
    IReadOnlyList<ProcessInfo> List();

    /// <summary>null — процесса нет или время старта недоступно.</summary>
    ProcessIdentity? Identify(int processId);
}
