using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Booster.Providers.Gpu.Nvidia;

// Структуры NVAPI в раскладке заголовков NVIDIA/nvapi (R560). Версия структуры = sizeof | (номер версии << 16);
// размеры и смещения проверяет NvApiStructLayoutTests. Все структуры передаются в NVAPI только указателем.

/// <summary>nvapi.h: NV_GPU_DYNAMIC_PSTATES_INFO_EX — загрузка по доменам.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NvDynamicPstatesInfoEx
{
    public const uint StructVersion = 72 | (1 << 16);
    // nvapi.h, описание NvAPI_GPU_GetDynamicPstatesInfoEx: 0 — GPU, 1 — FB, 2 — VID, 3 — BUS.
    public const int GpuDomain = 0;

    public uint Version;
    public uint Flags;
    public NvUtilizationDomains Utilization;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NvUtilizationDomain
{
    /// <summary>Бит 0 — bIsPresent.</summary>
    public uint Present;
    public uint Percentage;

    public readonly bool IsPresent => (Present & 1) != 0;
}

/// <summary>nvapi.h: NVAPI_MAX_GPU_UTILIZATIONS = 8.</summary>
[InlineArray(8)]
internal struct NvUtilizationDomains
{
    private NvUtilizationDomain _element;
}

/// <summary>nvapi.h: NV_GPU_THERMAL_SETTINGS_V2.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NvThermalSettings
{
    public const uint StructVersion = 68 | (2 << 16);
    // nvapi.h: NVAPI_THERMAL_TARGET_ALL — индекс датчика «все датчики».
    public const uint AllSensors = 15;
    // nvapi.h: NV_THERMAL_TARGET.
    public const int TargetGpu = 1;
    public const int TargetMemory = 2;
    public const int TargetPowerSupply = 4;
    public const int TargetBoard = 8;

    public uint Version;
    public uint Count;
    public NvThermalSensors Sensor;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NvThermalSensor
{
    public int Controller;
    public int DefaultMinTemp;
    public int DefaultMaxTemp;
    public int CurrentTemp;
    public int Target;
}

/// <summary>nvapi.h: NVAPI_MAX_THERMAL_SENSORS_PER_GPU = 3.</summary>
[InlineArray(3)]
internal struct NvThermalSensors
{
    private NvThermalSensor _element;
}

/// <summary>nvapi.h: NV_GPU_CLOCK_FREQUENCIES_V2, версия 3 (поле ClockType появилось с версии 2).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NvClockFrequencies
{
    public const uint StructVersion = 264 | (3 << 16);
    // nvapi.h: NV_GPU_CLOCK_FREQUENCIES_CLOCK_TYPE и NV_GPU_PUBLIC_CLOCK_ID.
    public const uint CurrentFrequency = 0;
    public const int GraphicsDomain = 0;
    public const int MemoryDomain = 4;

    public uint Version;
    /// <summary>Биты 0–3 — ClockType, остальные зарезервированы.</summary>
    public uint ClockType;
    public NvClockDomains Domain;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NvClockDomain
{
    /// <summary>Бит 0 — bIsPresent.</summary>
    public uint Present;
    public uint FrequencyKhz;

    public readonly bool IsPresent => (Present & 1) != 0;
}

/// <summary>nvapi.h: NVAPI_MAX_GPU_PUBLIC_CLOCKS = 32.</summary>
[InlineArray(32)]
internal struct NvClockDomains
{
    private NvClockDomain _element;
}

/// <summary>nvapi.h: NV_LOGICAL_GPU_DATA_V1 (pack 8). <see cref="OsAdapterId"/> указывает на буфер LUID вызывающего.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct NvLogicalGpuData
{
    public const uint StructVersion = 568 | (1 << 16);

    public uint Version;
    public nint OsAdapterId;
    public uint PhysicalGpuCount;
    public NvPhysicalGpuHandles PhysicalGpuHandles;
    public NvReservedWords Reserved;
}

/// <summary>nvapi_lite_common.h: NVAPI_MAX_PHYSICAL_GPUS = 64.</summary>
[InlineArray(MaxCount)]
internal struct NvPhysicalGpuHandles
{
    public const int MaxCount = 64;

    private nint _element;
}

[InlineArray(8)]
internal struct NvReservedWords
{
    private uint _element;
}

/// <summary>nvapi_lite_common.h: NvAPI_ShortString — ANSI-строка из 64 байт с завершающим нулём.</summary>
[InlineArray(Length)]
internal struct NvShortString
{
    public const int Length = 64;

    private byte _element;
}

/// <summary>nvapi_lite_common.h: NvAPI_UnicodeString — UTF-16 из 2048 символов с завершающим нулём.</summary>
[InlineArray(Length)]
internal struct NvUnicodeString
{
    public const int Length = 2048;

    private char _element;
}

/// <summary>nvapi.h: хвост объединения значения NVDRS_SETTING_V1 после первого NvU32 (всего 4100 байт: NVDRS_BINARY_SETTING).</summary>
[InlineArray(4096)]
internal struct NvDrsValueTail
{
    private byte _element;
}

/// <summary>nvapi.h: NVDRS_SETTING_V1. Значение читается только для типа DWORD.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct NvDrsSetting
{
    public const uint StructVersion = 12320 | (1 << 16);
    // nvapi.h: NVDRS_SETTING_TYPE.
    public const uint DwordType = 0;

    public uint Version;
    public NvUnicodeString SettingName;
    public uint SettingId;
    public uint SettingType;
    /// <summary>nvapi.h: NVDRS_SETTING_LOCATION — 0 текущий профиль, 1 глобальный, 2 базовый, 3 по умолчанию.</summary>
    public uint SettingLocation;
    public uint IsCurrentPredefined;
    public uint IsPredefinedValid;
    public uint PredefinedValue;
    public NvDrsValueTail PredefinedTail;
    public uint CurrentValue;
    public NvDrsValueTail CurrentTail;
}

/// <summary>nvapi.h: NVDRS_APPLICATION_V4. Передаётся только как выходной буфер NvAPI_DRS_FindApplicationByName.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct NvDrsApplication
{
    public const uint StructVersion = 20492 | (4 << 16);

    public uint Version;
    public uint IsPredefined;
    public NvUnicodeString AppName;
    public NvUnicodeString UserFriendlyName;
    public NvUnicodeString Launcher;
    public NvUnicodeString FileInFolder;
    /// <summary>Биты isMetro:1, isCommandLine:1.</summary>
    public uint Flags;
    public NvUnicodeString CommandLine;
}

/// <summary>nvapi.h: NVDRS_PROFILE_V1 — нужен только ради имени профиля.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct NvDrsProfile
{
    public const uint StructVersion = 4116 | (1 << 16);

    public uint Version;
    public NvUnicodeString ProfileName;
    /// <summary>NVDRS_GPU_SUPPORT: биты geforce:1, quadro:1, nvs:1.</summary>
    public uint GpuSupport;
    public uint IsPredefined;
    public uint NumOfApps;
    public uint NumOfSettings;
}

/// <summary>nvapi.h: NVAPI_GPU_PERF_DECREASE — флаги NvAPI_GPU_GetPerfDecreaseInfo.</summary>
internal static class NvPerfDecrease
{
    public const uint None = 0;
    public const uint Thermal = 0x1;
    public const uint PowerControl = 0x2;
    public const uint AcBattery = 0x4;
    public const uint ApiTriggered = 0x8;
    public const uint InsufficientPower = 0x10;
    public const uint Unknown = 0x80000000;
}
