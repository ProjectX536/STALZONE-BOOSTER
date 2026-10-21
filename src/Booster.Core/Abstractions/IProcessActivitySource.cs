namespace Booster.Core.Abstractions;

/// <summary>Процент от всех логических CPU за интервал между замерами.</summary>
public sealed record ProcessCpuUsage(string Name, int ProcessId, double CpuPercent);

/// <param name="CpuPercent">Процент от всех логических CPU за интервал.</param>
/// <param name="BusiestThreadPercent">Самый загруженный поток, % одного логического CPU.</param>
public sealed record TargetCpuUsage(string Name, int ProcessId, double CpuPercent, double BusiestThreadPercent, int ThreadCount);

/// <param name="SystemCpuPercent">Загрузка всех логических CPU: 100 − доля простоя.</param>
/// <param name="TopOthers">Самые загруженные процессы, кроме цели и простоя, по убыванию.</param>
public sealed record ProcessActivity(Reading<double> SystemCpuPercent, Reading<TargetCpuUsage> Target, IReadOnlyList<ProcessCpuUsage> TopOthers);

/// <summary>Время CPU процессов и потоков без открытия процессов. Каждый вызов — разность с прошлым.</summary>
public interface IProcessActivitySource
{
    /// <summary>Цель — ровно одно из имени и PID. Первый вызов и первый после появления цели — Unknown «нужен второй замер».</summary>
    ProcessActivity Sample(string? processName, int? processId, int topCount);
}
