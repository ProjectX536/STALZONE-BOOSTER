using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Hardware;

public sealed class RamDetector(ICimClient cim) : IInventoryDetector<MemoryInventory>
{
    private const string ModulesQuery =
        "SELECT Capacity, SMBIOSMemoryType, ConfiguredClockSpeed, Speed, Manufacturer, PartNumber, DeviceLocator, BankLabel " +
        "FROM Win32_PhysicalMemory";
    private const string ArraysQuery = "SELECT MemoryDevices, Use FROM Win32_PhysicalMemoryArray";
    private const string TotalQuery = "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem";

    public string SectionId => SectionIds.Memory;

    public async Task<MemoryInventory> DetectAsync(CancellationToken ct)
    {
        var modules = await cim.QueryAsync(WmiNamespaces.CimV2, ModulesQuery, ct).ConfigureAwait(false);
        var arrays = await cim.QueryAsync(WmiNamespaces.CimV2, ArraysQuery, ct).ConfigureAwait(false);
        var system = await cim.QueryAsync(WmiNamespaces.CimV2, TotalQuery, ct).ConfigureAwait(false);
        return RamMapper.Map(modules, arrays, system.FirstOrDefault());
    }
}
