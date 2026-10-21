using Booster.Core.Abstractions;

namespace Booster.Core.Inventory;

public sealed record CpuPackage(
    string Name,
    Reading<string> Manufacturer,
    Reading<int> Cores,
    Reading<int> LogicalProcessors,
    Reading<int> BaseClockMhz);

public sealed record CoreTopology(int PerformanceCores, int EfficiencyCores, bool IsHybrid);

public sealed record CpuInventory(IReadOnlyList<CpuPackage> Packages, Reading<CoreTopology> Topology);
