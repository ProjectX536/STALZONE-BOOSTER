namespace Booster.Core.Abstractions;

/// <summary>Память процесса из снимка системы.</summary>
/// <param name="PrivateBytes">Частная выделенная память (commit) процесса.</param>
public sealed record ProcessMemoryUsage(string Name, int ProcessId, long WorkingSetBytes, long PrivateBytes);

/// <summary>Ввод-вывод и жёсткие ошибки страниц процесса между двумя замерами.</summary>
public sealed record ProcessIoUsage(string Name, int ProcessId, double ReadBytesPerSecond, double WriteBytesPerSecond,
    double HardFaultsPerSecond);

/// <param name="TopIo">По убыванию чтения + записи.</param>
/// <param name="TopHardFaults">По убыванию жёстких ошибок страниц.</param>
public sealed record ProcessIoActivity(IReadOnlyList<ProcessIoUsage> TopIo, IReadOnlyList<ProcessIoUsage> TopHardFaults);

/// <summary>Память и ввод-вывод процессов одним снимком системы, без открытия процессов.</summary>
public interface IProcessResourceSource
{
    /// <summary>Процессы с наибольшей частной памятью, по убыванию, без Idle.</summary>
    /// <exception cref="InvalidDataException">Снимок процессов не получен или не разобран.</exception>
    /// <exception cref="PlatformNotSupportedException">Раскладка снимка известна только для 64-битного процесса.</exception>
    IReadOnlyList<ProcessMemoryUsage> ReadMemory(int topCount);

    /// <summary>
    /// Разность с прошлым вызовом: процессы с ненулевой активностью. Первый вызов — Unknown «нужен второй замер»;
    /// сбой снимка — Failed.
    /// </summary>
    Reading<ProcessIoActivity> SampleIo(int topCount);
}
