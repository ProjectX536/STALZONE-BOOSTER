using Booster.Core.Abstractions;

namespace Booster.Core.Inventory;

public enum ChannelLabelHint
{
    None,
    DifferentChannelsByLabel,
    SameChannelByLabel,
}

public sealed record MemoryModule(
    Reading<long> CapacityBytes,
    Reading<string> MemoryType,
    Reading<int> RatedSpeedMts,
    Reading<int> ConfiguredSpeedMts,
    Reading<string> Manufacturer,
    Reading<string> PartNumber,
    Reading<string> Locator,
    Reading<string> BankLabel);

public sealed record MemoryInventory(
    Reading<long> TotalPhysicalBytes,
    IReadOnlyList<MemoryModule> Modules,
    Reading<int> SlotCount,
    Reading<bool> DualChannel,
    ChannelLabelHint ChannelHint);
