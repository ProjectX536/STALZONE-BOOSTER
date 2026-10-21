namespace Booster.Core.Abstractions;

/// <param name="CpuPercent">Загрузка всех логических процессоров, 0–100; null — недоступно.</param>
/// <param name="MemoryLoadPercent">Занятая физическая память, 0–100; null — недоступно.</param>
public sealed record SystemLoad(double? CpuPercent, double? MemoryLoadPercent);

/// <summary>Лёгкий замер нагрузки системы во время игровой сессии.</summary>
public interface ISystemLoadSource
{
    /// <summary>CPU — за время с прошлого вызова; при первом вызове null.</summary>
    SystemLoad Sample();
}
