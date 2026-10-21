namespace Booster.Core.Abstractions;

public sealed record SystemLoad(double? CpuPercent, double? MemoryLoadPercent);

public interface ISystemLoadSource
{
    SystemLoad Sample();
}
