using Booster.Providers.Gpu.Nvidia;

namespace Booster.Providers.Gpu.Tests.Nvidia;

public sealed class NvApiLibraryTests
{
    [Fact]
    public void Missing_library_gives_null_with_library_not_found()
    {
        var api = new NvApiLibraryFactory("booster-missing-nvapi64.dll").TryCreate(out var status);

        Assert.Null(api);
        Assert.Equal(NvApiStatus.LibraryNotFound, status);
        Assert.Equal(-2, status);
    }

    [Fact]
    public void Driver_version_is_formatted_as_major_dot_minor()
    {
        Assert.Equal("560.94", NvApiLibrary.FormatDriverVersion(56094));
        Assert.Equal("581.08", NvApiLibrary.FormatDriverVersion(58108));
    }

    [Fact]
    public void Pci_identifiers_are_split_into_vendor_and_device()
    {
        // NvAPI_GPU_GetPCIIdentifiers: pDeviceId = (DEV << 16) | VEN, pSubSystemId — как SUBSYS_ в Hardware ID.
        var pci = NvApiLibrary.ToPciId(deviceId: 0x248610DE, subSystemId: 0x40011458, revisionId: 0xA1);

        Assert.Equal("VEN_10DE&DEV_2486&SUBSYS_40011458&REV_A1", pci.ToString());
    }
}
