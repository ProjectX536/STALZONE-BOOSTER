using Booster.Core.Abstractions;
using Booster.Core.Inventory;

namespace Booster.Platform.Windows.Monitoring;

public sealed record GpuCounterSample(Reading<double> UtilizationPercent, Reading<long> DedicatedUsageBytes);

public interface IGpuSystemCounterSource
{
    IReadOnlyDictionary<AdapterLuid, GpuCounterSample> Sample();

    Reading<bool> IsAvailable { get; }
}
