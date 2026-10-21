using Booster.Core.Inventory;

namespace Booster.Platform.Windows.Monitoring;

public sealed record GpuEngineSample(AdapterLuid Luid, string EngineType, double UtilizationPercent);

/// <summary>Загрузка движков GPU одним процессом; подменяется в тестах.</summary>
public interface IGpuEngineCounterSource
{
    /// <summary>
    /// Загрузка с прошлого вызова для этого процесса. Движки, которые появились с прошлого вызова,
    /// в результат ещё не попадают: для процента нужны два чтения.
    /// </summary>
    IReadOnlyList<GpuEngineSample> Sample(int processId);
}
