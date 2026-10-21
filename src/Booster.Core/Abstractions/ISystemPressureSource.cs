namespace Booster.Core.Abstractions;

/// <summary>Память — GlobalMemoryStatusEx; чтения страниц и диск — счётчики Memory и PhysicalDisk(_Total).</summary>
/// <param name="CommitPercent">Выделенная память от предела выделения (физическая память + файл подкачки), %.</param>
/// <param name="DiskBusyPercent">100 − доля простоя всех физических дисков.</param>
/// <param name="DiskLatencyMs">Среднее время одной операции диска.</param>
/// <param name="DpcTimePercent">Processor(_Total)\% DPC Time: доля времени всех логических CPU, %.</param>
/// <param name="InterruptTimePercent">Processor(_Total)\% Interrupt Time, %.</param>
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
    /// <summary>Счётчики-скорости при первом вызове — Unknown «нужен второй замер».</summary>
    SystemPressure Sample();
}
