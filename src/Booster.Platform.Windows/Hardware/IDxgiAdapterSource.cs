using Booster.Core.Inventory;

namespace Booster.Platform.Windows.Hardware;

public sealed record DxgiAdapterRecord(string Description, uint VendorId, uint DeviceId, uint SubSysId, uint Revision,
    AdapterLuid Luid, ulong DedicatedVideoMemory, ulong SharedSystemMemory, bool IsSoftware, int HighPerformanceRank, bool? IsUma);

public interface IDxgiAdapterSource
{
    IReadOnlyList<DxgiAdapterRecord> Enumerate();
}
