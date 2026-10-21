using System.Runtime.CompilerServices;
using Booster.Providers.Gpu.Nvidia;

namespace Booster.Providers.Gpu.Tests.Nvidia;

public sealed class NvApiStructLayoutTests
{
    // Размеры и версии — из заголовков NVIDIA/nvapi (R560): версия = sizeof | (номер << 16).
    public static TheoryData<string, int, uint, int, uint> Layouts => new()
    {
        { "NV_GPU_DYNAMIC_PSTATES_INFO_EX", Unsafe.SizeOf<NvDynamicPstatesInfoEx>(), NvDynamicPstatesInfoEx.StructVersion, 72, 0x10048 },
        { "NV_GPU_THERMAL_SETTINGS_V2", Unsafe.SizeOf<NvThermalSettings>(), NvThermalSettings.StructVersion, 68, 0x20044 },
        { "NV_GPU_CLOCK_FREQUENCIES_V2", Unsafe.SizeOf<NvClockFrequencies>(), NvClockFrequencies.StructVersion, 264, 0x30108 },
        { "NV_LOGICAL_GPU_DATA_V1", Unsafe.SizeOf<NvLogicalGpuData>(), NvLogicalGpuData.StructVersion, 568, 0x10238 },
        { "NVDRS_SETTING_V1", Unsafe.SizeOf<NvDrsSetting>(), NvDrsSetting.StructVersion, 12320, 0x13020 },
        { "NVDRS_APPLICATION_V4", Unsafe.SizeOf<NvDrsApplication>(), NvDrsApplication.StructVersion, 20492, 0x4500C },
        { "NVDRS_PROFILE_V1", Unsafe.SizeOf<NvDrsProfile>(), NvDrsProfile.StructVersion, 4116, 0x11014 },
    };

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Sizes_match_the_nvapi_headers(string header, int size, uint version, int expectedSize, uint expectedVersion)
    {
        Assert.Equal((header, expectedSize, expectedVersion), (header, size, version));
    }

    [Fact]
    public void Drs_setting_offsets_match()
    {
        var setting = default(NvDrsSetting);
        ref var start = ref Unsafe.As<NvDrsSetting, byte>(ref setting);

        Assert.Equal(4, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<NvUnicodeString, byte>(ref setting.SettingName)));
        Assert.Equal(4100, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<uint, byte>(ref setting.SettingId)));
        Assert.Equal(4104, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<uint, byte>(ref setting.SettingType)));
        Assert.Equal(4108, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<uint, byte>(ref setting.SettingLocation)));
        Assert.Equal(4112, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<uint, byte>(ref setting.IsCurrentPredefined)));
        Assert.Equal(4120, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<uint, byte>(ref setting.PredefinedValue)));
        Assert.Equal(8220, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<uint, byte>(ref setting.CurrentValue)));
    }

    [Fact]
    public void Logical_gpu_data_offsets_match()
    {
        // pack 8: указатель на буфер LUID выровнен по 8, массив дескрипторов начинается с 24.
        var data = default(NvLogicalGpuData);
        ref var start = ref Unsafe.As<NvLogicalGpuData, byte>(ref data);

        Assert.Equal(8, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<nint, byte>(ref data.OsAdapterId)));
        Assert.Equal(16, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<uint, byte>(ref data.PhysicalGpuCount)));
        Assert.Equal(24, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<NvPhysicalGpuHandles, byte>(ref data.PhysicalGpuHandles)));
    }

    [Fact]
    public void Drs_profile_and_application_name_offsets_match()
    {
        var profile = default(NvDrsProfile);
        var application = default(NvDrsApplication);

        Assert.Equal(4, (int)Unsafe.ByteOffset(ref Unsafe.As<NvDrsProfile, byte>(ref profile),
            ref Unsafe.As<NvUnicodeString, byte>(ref profile.ProfileName)));
        Assert.Equal(8, (int)Unsafe.ByteOffset(ref Unsafe.As<NvDrsApplication, byte>(ref application),
            ref Unsafe.As<NvUnicodeString, byte>(ref application.AppName)));
    }
}
