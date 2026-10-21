using Booster.Core.Inventory;

namespace Booster.Platform.Windows.Monitoring;

public sealed record GpuEngineSample(AdapterLuid Luid, string EngineType, double UtilizationPercent);

public interface IGpuEngineCounterSource
{
    IReadOnlyList<GpuEngineSample> Sample(int processId);
}
