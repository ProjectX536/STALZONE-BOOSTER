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

/// <summary>
/// Видеоадаптер. Объём видеопамяти берётся только из DXGI: <c>Win32_VideoController.AdapterRAM</c>
/// 32-битный и не превышает 4 ГБ. <see cref="MatchNote"/> объясняет неоднозначное сопоставление источников.
/// </summary>
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

/// <summary>Выбор GPU для приложения в «Параметры → Дисплей → Графика».</summary>
public sealed record AppGpuPreference(string ExecutablePath, AppGpuPreferenceKind Preference);

public sealed record GpuInventory(
    IReadOnlyList<GpuAdapterInfo> Adapters,
    Reading<bool> IsHybrid,
    Reading<IReadOnlyList<AppGpuPreference>> AppPreferences);
