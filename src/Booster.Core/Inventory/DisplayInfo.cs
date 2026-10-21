using Booster.Core.Abstractions;

namespace Booster.Core.Inventory;

/// <summary>Активный путь вывода. <see cref="AdapterLuid"/> — адаптер, к которому подключён монитор.</summary>
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
