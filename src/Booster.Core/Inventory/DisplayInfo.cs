using Booster.Core.Abstractions;

namespace Booster.Core.Inventory;

public sealed record DisplayInfo(
    Reading<string> MonitorName,
    Reading<int> Width,
    Reading<int> Height,
    Reading<double> RefreshRateHz,
    Reading<bool> HdrSupported,
    Reading<bool> HdrEnabled,
    Reading<bool> VrrEnabled,
    Reading<AdapterLuid> AdapterLuid,
    bool IsPrimary);
