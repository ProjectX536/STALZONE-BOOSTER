using Booster.Core.Abstractions;

namespace Booster.Core.Inventory;

/// <summary>Физический процессор (сокет). <see cref="BaseClockMhz"/> — базовая частота, а не частота под нагрузкой.</summary>
public sealed record CpuPackage(
    string Name,
    Reading<string> Manufacturer,
    Reading<int> Cores,
    Reading<int> LogicalProcessors,
    Reading<int> BaseClockMhz);

/// <summary>Число производительных и энергоэффективных ядер по EfficiencyClass.</summary>
public sealed record CoreTopology(int PerformanceCores, int EfficiencyCores, bool IsHybrid);

public sealed record CpuInventory(IReadOnlyList<CpuPackage> Packages, Reading<CoreTopology> Topology);
