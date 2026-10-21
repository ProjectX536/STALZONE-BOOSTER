using Booster.Core.Inventory;
using Booster.Platform.Windows.Hardware;

namespace Booster.Platform.Windows.Tests.Hardware;

public class PciVendorsTests
{
    [Theory]
    [InlineData(0x10DEu, GpuVendor.Nvidia)]
    [InlineData(0x1002u, GpuVendor.Amd)]
    [InlineData(0x1022u, GpuVendor.Amd)]
    [InlineData(0x8086u, GpuVendor.Intel)]
    [InlineData(0x1414u, GpuVendor.Microsoft)]
    [InlineData(0x1234u, GpuVendor.Other)]
    public void Vendor_id_is_named(uint vendorId, GpuVendor expected) =>
        Assert.Equal(expected, PciVendors.FromId(vendorId));

    [Fact]
    public void Pnp_id_of_pci_device_is_parsed()
    {
        Assert.True(PciVendors.TryParsePnpId(@"PCI\VEN_10DE&DEV_2489&SUBSYS_C9721462&REV_A1\4&12033776&0&0008", out var id));

        Assert.Equal(new PciId(0x10DE, 0x2489, 0xC9721462, 0xA1), id);
    }

    [Fact]
    public void Pnp_id_is_case_insensitive_and_revision_is_optional()
    {
        Assert.True(PciVendors.TryParsePnpId(@"pci\ven_8086&dev_a7a8&subsys_00011234", out var id));

        Assert.Equal(new PciId(0x8086, 0xA7A8, 0x00011234, 0), id);
    }

    [Theory]
    [InlineData(@"ROOT\BasicRender\0000")]
    [InlineData(@"SWD\REMOTEDISPLAYENUM\RDPIDD")]
    [InlineData(@"PCI\VEN_10DE\4&1")]
    [InlineData(@"PCI\VEN_ZZZZ&DEV_2489")]
    [InlineData("")]
    [InlineData(null)]
    public void Non_pci_or_broken_pnp_id_is_rejected(string? pnpDeviceId) =>
        Assert.False(PciVendors.TryParsePnpId(pnpDeviceId, out _));
}
