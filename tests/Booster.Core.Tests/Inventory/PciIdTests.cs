using Booster.Core.Inventory;

namespace Booster.Core.Tests.Inventory;

public class PciIdTests
{
    [Fact]
    public void ToString_matches_hardware_id_format()
    {
        Assert.Equal("VEN_10DE&DEV_2489&SUBSYS_88AC1462&REV_A1", new PciId(0x10DE, 0x2489, 0x88AC1462, 0xA1).ToString());
    }

    [Fact]
    public void ToString_pads_with_zeros()
    {
        Assert.Equal("VEN_8086&DEV_0046&SUBSYS_00000000&REV_02", new PciId(0x8086, 0x46, 0, 2).ToString());
    }
}
