using Booster.Core.Abstractions;

namespace Booster.Core.Inventory;

public enum GpuVendor
{
    Unknown,
    Nvidia,
    Amd,
    Intel,
    Microsoft,
    Other,
}

public enum GpuAdapterKind
{
    Integrated,
    Discrete,
}

public sealed record GpuAdapterInfo(
    string Name,
    GpuVendor Vendor,
    Reading<PciId> Pci,
    Reading<AdapterLuid> Luid,
    Reading<GpuAdapterKind> Kind,
    Reading<long> DedicatedVideoMemoryBytes,
    Reading<long> SharedSystemMemoryBytes,
    Reading<string> DriverVersion,
    Reading<DateOnly> DriverDate,
    Reading<int> HighPerformanceRank,
    bool IsSoftware,
    string? MatchNote);

public enum AppGpuPreferenceKind
{
    SystemDefault,
    PowerSaving,
    HighPerformance,
}

public sealed record AppGpuPreference(string ExecutablePath, AppGpuPreferenceKind Preference);

public sealed record GpuInventory(
    IReadOnlyList<GpuAdapterInfo> Adapters,
    Reading<bool> IsHybrid,
    Reading<IReadOnlyList<AppGpuPreference>> AppPreferences);
