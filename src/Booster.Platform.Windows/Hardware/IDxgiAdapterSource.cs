using Booster.Core.Inventory;

namespace Booster.Platform.Windows.Hardware;

/// <param name="HighPerformanceRank">Позиция в <c>EnumAdapterByGpuPreference(HIGH_PERFORMANCE)</c>; −1 — порядок неизвестен.</param>
/// <param name="IsUma">Унифицированная память по D3D12 <c>FEATURE_ARCHITECTURE</c>; <c>null</c> — D3D12 недоступен.</param>
public sealed record DxgiAdapterRecord(string Description, uint VendorId, uint DeviceId, uint SubSysId, uint Revision,
    AdapterLuid Luid, ulong DedicatedVideoMemory, ulong SharedSystemMemory, bool IsSoftware, int HighPerformanceRank, bool? IsUma);

public interface IDxgiAdapterSource
{
    IReadOnlyList<DxgiAdapterRecord> Enumerate();
}
