using Booster.Core.Abstractions;

namespace Booster.Core.Inventory;

public sealed record HardwareInventory(
    DateTimeOffset CollectedAt,
    Reading<CpuInventory> Cpu,
    Reading<GpuInventory> Gpu,
    Reading<MemoryInventory> Memory,
    Reading<SystemBoardInfo> Board,
    Reading<OsInfo> Os,
    Reading<IReadOnlyList<StorageDeviceInfo>> Storage,
    Reading<IReadOnlyList<DisplayInfo>> Displays);
