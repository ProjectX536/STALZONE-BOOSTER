using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Booster.Providers.Gpu.Nvidia;

// Раскладка из заголовков NVIDIA/nvapi (R560), версия = sizeof | (номер << 16).

[StructLayout(LayoutKind.Sequential)]
internal struct NvDynamicPstatesInfoEx
{
    public const uint StructVersion = 72 | (1 << 16);
    // 0 — GPU, 1 — FB, 2 — VID, 3 — BUS.
    public const int GpuDomain = 0;

    public uint Version;
    public uint Flags;
    public NvUtilizationDomains Utilization;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NvUtilizationDomain
{
    public uint Present;
    public uint Percentage;

    public readonly bool IsPresent => (Present & 1) != 0;
}

[InlineArray(8)]
internal struct NvUtilizationDomains
{
    private NvUtilizationDomain _element;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NvThermalSettings
{
    public const uint StructVersion = 68 | (2 << 16);
    public const uint AllSensors = 15;
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

[InlineArray(3)]
internal struct NvThermalSensors
{
    private NvThermalSensor _element;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NvClockFrequencies
{
    public const uint StructVersion = 264 | (3 << 16);
    public const uint CurrentFrequency = 0;
    public const int GraphicsDomain = 0;
    public const int MemoryDomain = 4;

    public uint Version;
    public uint ClockType;
    public NvClockDomains Domain;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NvClockDomain
{
    public uint Present;
    public uint FrequencyKhz;

    public readonly bool IsPresent => (Present & 1) != 0;
}

[InlineArray(32)]
internal struct NvClockDomains
{
    private NvClockDomain _element;
}

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

[InlineArray(Length)]
internal struct NvShortString
{
    public const int Length = 64;

    private byte _element;
}

[InlineArray(Length)]
internal struct NvUnicodeString
{
    public const int Length = 2048;

    private char _element;
}

[InlineArray(4096)]
internal struct NvDrsValueTail
{
    private byte _element;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct NvDrsSetting
{
    public const uint StructVersion = 12320 | (1 << 16);
    public const uint DwordType = 0;

    public uint Version;
    public NvUnicodeString SettingName;
    public uint SettingId;
    public uint SettingType;
    public uint SettingLocation;
    public uint IsCurrentPredefined;
    public uint IsPredefinedValid;
    public uint PredefinedValue;
    public NvDrsValueTail PredefinedTail;
    public uint CurrentValue;
    public NvDrsValueTail CurrentTail;
}

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
    public uint Flags;
    public NvUnicodeString CommandLine;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct NvDrsProfile
{
    public const uint StructVersion = 4116 | (1 << 16);

    public uint Version;
    public NvUnicodeString ProfileName;
    public uint GpuSupport;
    public uint IsPredefined;
    public uint NumOfApps;
    public uint NumOfSettings;
}

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
