namespace Booster.Core.Abstractions;

public sealed record ProcessCpuUsage(string Name, int ProcessId, double CpuPercent);

public sealed record TargetCpuUsage(string Name, int ProcessId, double CpuPercent, double BusiestThreadPercent, int ThreadCount);

public sealed record ProcessActivity(Reading<double> SystemCpuPercent, Reading<TargetCpuUsage> Target, IReadOnlyList<ProcessCpuUsage> TopOthers);

public interface IProcessActivitySource
{
    ProcessActivity Sample(string? processName, int? processId, int topCount);
}
