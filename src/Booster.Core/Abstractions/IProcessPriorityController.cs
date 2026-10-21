namespace Booster.Core.Abstractions;

/// <summary>Классы приоритета Windows, по возрастанию.</summary>
public enum ProcessPriority
{
    Idle,
    BelowNormal,
    Normal,
    AboveNormal,
    High,
    RealTime,
}

/// <summary>Приоритет процесса, опознанного по PID и времени старта: PID может достаться другому процессу.</summary>
public interface IProcessPriorityController
{
    /// <returns>null — процесса с этой парой PID и времени старта нет или нет доступа.</returns>
    ProcessPriority? GetPriority(ProcessIdentity process);

    /// <summary>Открывает процесс с правом смены приоритета и сразу закрывает; ничего не меняет.</summary>
    bool CanSetPriority(ProcessIdentity process);

    /// <exception cref="ArgumentOutOfRangeException"><see cref="ProcessPriority.RealTime"/>.</exception>
    /// <exception cref="InvalidOperationException">PID принадлежит уже другому процессу.</exception>
    /// <exception cref="UnauthorizedAccessException">
    /// Система отказала в доступе (ERROR_ACCESS_DENIED): процесс защищён или запущен от администратора. Приоритет не изменён.
    /// </exception>
    void SetPriority(ProcessIdentity process, ProcessPriority priority);
}
