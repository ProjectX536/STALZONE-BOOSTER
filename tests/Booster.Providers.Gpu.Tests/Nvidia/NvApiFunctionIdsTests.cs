using System.Reflection;
using Booster.Providers.Gpu.Nvidia;

namespace Booster.Providers.Gpu.Tests.Nvidia;

public sealed class NvApiFunctionIdsTests
{
    private static readonly Dictionary<string, uint> Whitelist = new()
    {
        ["Initialize"] = 0x0150E828,
        ["Unload"] = 0xD22BDD7E,
        ["EnumPhysicalGpus"] = 0xE5AC921F,
        ["GpuGetFullName"] = 0xCEEE8E9F,
        ["GpuGetBusId"] = 0x1BE0B8E5,
        ["GpuGetPciIdentifiers"] = 0x2DDFB66E,
        ["GpuGetDynamicPstatesInfoEx"] = 0x60DED2ED,
        ["GpuGetThermalSettings"] = 0xE3640A56,
        ["GpuGetAllClockFrequencies"] = 0xDCB616C3,
        ["GpuGetPerfDecreaseInfo"] = 0x7F7F4600,
        ["GpuGetTachReading"] = 0x5F608315,
        ["SysGetDriverAndBranchVersion"] = 0x2926AAAD,
        ["GetLogicalGpuFromPhysicalGpu"] = 0xADD604D1,
        ["GpuGetLogicalGpuInfo"] = 0x842B066E,
        ["GpuGetAdapterIdFromPhysicalGpu"] = 0x0FF07FDE,
        ["DrsCreateSession"] = 0x0694D52E,
        ["DrsLoadSettings"] = 0x375DBD6B,
        ["DrsGetCurrentGlobalProfile"] = 0x617BFF9F,
        ["DrsFindApplicationByName"] = 0xEEE566B2,
        ["DrsGetProfileInfo"] = 0x61CD6FD6,
        ["DrsGetSetting"] = 0x73BF8338,
        ["DrsDestroySession"] = 0xDAD9CFF8,
    };

    private static Dictionary<string, uint> Bound() =>
        typeof(NvApiFunctionIds).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .ToDictionary(f => f.Name, f => (uint)f.GetRawConstantValue()!);

    [Fact]
    public void Only_whitelisted_functions_are_bound()
    {
        Assert.Equal(Whitelist.OrderBy(p => p.Key, StringComparer.Ordinal), Bound().OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void Write_functions_are_absent()
    {
        var values = Bound().Values;

        Assert.DoesNotContain(0x577DD202u, values); // NvAPI_DRS_SetSetting
        Assert.DoesNotContain(0xFCBC7E14u, values); // NvAPI_DRS_SaveSettings
    }
}
