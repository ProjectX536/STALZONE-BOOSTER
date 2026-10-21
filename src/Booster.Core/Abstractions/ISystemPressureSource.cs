namespace Booster.Core.Abstractions;

public sealed record SystemPressure(
    Reading<double> AvailableMemoryPercent,
    Reading<double> CommitPercent,
    Reading<double> PageReadsPerSecond,
    Reading<double> DiskBusyPercent,
    Reading<double> DiskLatencyMs,
    Reading<double> DpcTimePercent,
    Reading<double> InterruptTimePercent);

public interface ISystemPressureSource
{
    SystemPressure Sample();
}
