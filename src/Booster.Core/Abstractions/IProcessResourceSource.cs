namespace Booster.Core.Abstractions;

public sealed record ProcessMemoryUsage(string Name, int ProcessId, long WorkingSetBytes, long PrivateBytes);

public sealed record ProcessIoUsage(string Name, int ProcessId, double ReadBytesPerSecond, double WriteBytesPerSecond,
    double HardFaultsPerSecond);

public sealed record ProcessIoActivity(IReadOnlyList<ProcessIoUsage> TopIo, IReadOnlyList<ProcessIoUsage> TopHardFaults);

public interface IProcessResourceSource
{
    IReadOnlyList<ProcessMemoryUsage> ReadMemory(int topCount);

    Reading<ProcessIoActivity> SampleIo(int topCount);
}
