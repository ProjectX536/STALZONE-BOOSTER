using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Hardware;
using Booster.Platform.Windows.Tests.TestSupport;

namespace Booster.Platform.Windows.Tests.Hardware;

public class MotherboardMapperTests
{
    private static readonly Reading<FirmwareKind> Uefi = Reading<FirmwareKind>.Known(FirmwareKind.Uefi, "test");

    private static SystemBoardInfo RealMachine() => MotherboardMapper.Map(
        WmiFixture.LoadSingle("desktop-nvidia/Win32_ComputerSystem.json"),
        WmiFixture.LoadSingle("desktop-nvidia/Win32_BaseBoard.json"),
        WmiFixture.LoadSingle("desktop-nvidia/Win32_BIOS.json"),
        WmiFixture.LoadSingle("desktop-nvidia/Win32_SystemEnclosure.json"),
        Uefi);

    [Fact]
    public void Real_machine_is_an_msi_desktop()
    {
        var board = RealMachine();

        Assert.Equal("Micro-Star International Co., Ltd.", board.SystemManufacturer.Value);
        Assert.Equal("MS-7D48", board.SystemModel.Value);
        Assert.Equal("Micro-Star International Co., Ltd.", board.BoardManufacturer.Value);
        Assert.Equal("PRO B760M-E DDR4 (MS-7D48)", board.BoardProduct.Value);
        Assert.Equal(PcFormFactor.Desktop, board.FormFactor.Value);
        Assert.Equal("WMI Win32_ComputerSystem.PCSystemType", board.FormFactor.Source);
        Assert.Equal("American Megatrends International, LLC.", board.BiosVendor.Value);
        Assert.Equal("B.D0", board.BiosVersion.Value);
        Assert.Equal(FirmwareKind.Uefi, board.Firmware.Value);
    }

    [Fact]
    public void Bios_release_date_is_parsed_from_dmtf()
    {
        var date = RealMachine().BiosReleaseDate;

        Assert.Equal(new DateOnly(2024, 9, 26), date.Value);
        Assert.Equal("WMI Win32_BIOS.ReleaseDate", date.Source);
    }

    [Fact]
    public void Placeholder_board_product_is_unknown()
    {
        var board = MotherboardMapper.Map(null, WmiFixture.Row(("Manufacturer", "ASUSTeK"), ("Product", "Default string")),
            null, null, Uefi);

        Assert.Equal("ASUSTeK", board.BoardManufacturer.Value);
        Assert.Equal(DataStatus.Unknown, board.BoardProduct.Status);
    }

    [Fact]
    public void Placeholder_system_model_is_unknown()
    {
        var system = WmiFixture.Row(("Manufacturer", "System manufacturer"), ("Model", "System Product Name"),
            ("PCSystemType", 1L));

        var board = MotherboardMapper.Map(system, null, null, null, Uefi);

        Assert.Equal(DataStatus.Unknown, board.SystemModel.Status);
        Assert.Equal(DataStatus.Unknown, board.SystemManufacturer.Status);
        Assert.Equal(PcFormFactor.Desktop, board.FormFactor.Value);
    }

    [Fact]
    public void Synthetic_laptop_is_a_laptop()
    {
        var board = MotherboardMapper.Map(
            WmiFixture.LoadSingle("laptop-hybrid.synthetic/Win32_ComputerSystem.json"), null, null,
            WmiFixture.LoadSingle("laptop-hybrid.synthetic/Win32_SystemEnclosure.json"), Uefi);

        Assert.Equal(PcFormFactor.Laptop, board.FormFactor.Value);
        Assert.Equal("Contoso Laptop 16 G5", board.SystemModel.Value);
    }

    [Theory]
    [InlineData(1, PcFormFactor.Desktop)]
    [InlineData(3, PcFormFactor.Desktop)]
    [InlineData(2, PcFormFactor.Laptop)]
    public void Pc_system_type_decides_form_factor(int pcSystemType, PcFormFactor expected)
    {
        var formFactor = MotherboardMapper.MapFormFactor(pcSystemType, [10]);

        Assert.Equal(expected, formFactor.Value);
        Assert.Equal("WMI Win32_ComputerSystem.PCSystemType", formFactor.Source);
    }

    [Theory]
    [InlineData(10, PcFormFactor.Laptop)]
    [InlineData(31, PcFormFactor.Laptop)]
    [InlineData(3, PcFormFactor.Desktop)]
    [InlineData(35, PcFormFactor.Desktop)]
    public void Chassis_type_decides_when_pc_system_type_is_zero(int chassis, PcFormFactor expected)
    {
        var formFactor = MotherboardMapper.MapFormFactor(0, [chassis]);

        Assert.Equal(expected, formFactor.Value);
        Assert.Equal("WMI Win32_SystemEnclosure.ChassisTypes", formFactor.Source);
    }

    [Fact]
    public void Missing_pc_system_type_falls_back_to_chassis() =>
        Assert.Equal(PcFormFactor.Laptop, MotherboardMapper.MapFormFactor(null, [9]).Value);

    [Fact]
    public void Server_pc_system_type_falls_back_to_chassis() =>
        Assert.Equal(PcFormFactor.Desktop, MotherboardMapper.MapFormFactor(4, [7]).Value);

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 2 })]
    [InlineData(new[] { 17, 23 })]
    [InlineData(new[] { 3, 10 })]
    public void Unrecognized_or_conflicting_chassis_is_unknown(int[] chassis) =>
        Assert.Equal(DataStatus.Unknown, MotherboardMapper.MapFormFactor(0, chassis).Status);

    [Fact]
    public void Missing_bios_row_leaves_bios_fields_unknown()
    {
        var board = MotherboardMapper.Map(
            WmiFixture.LoadSingle("desktop-nvidia/Win32_ComputerSystem.json"),
            WmiFixture.LoadSingle("desktop-nvidia/Win32_BaseBoard.json"),
            null,
            WmiFixture.LoadSingle("desktop-nvidia/Win32_SystemEnclosure.json"),
            Uefi);

        Assert.Equal(DataStatus.Unknown, board.BiosVendor.Status);
        Assert.Equal(DataStatus.Unknown, board.BiosVersion.Status);
        Assert.Equal(DataStatus.Unknown, board.BiosReleaseDate.Status);
        Assert.Equal("MS-7D48", board.SystemModel.Value);
        Assert.Equal("PRO B760M-E DDR4 (MS-7D48)", board.BoardProduct.Value);
        Assert.Equal(PcFormFactor.Desktop, board.FormFactor.Value);
    }

    [Theory]
    [InlineData(1u, FirmwareKind.LegacyBios)]
    [InlineData(2u, FirmwareKind.Uefi)]
    public void Firmware_type_is_mapped(uint value, FirmwareKind expected)
    {
        var firmware = FirmwareTypeSource.Map(value);

        Assert.Equal(expected, firmware.Value);
        Assert.Equal(FirmwareTypeSource.Source, firmware.Source);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(3u)]
    public void Unknown_firmware_type_is_unknown(uint value) =>
        Assert.Equal(DataStatus.Unknown, FirmwareTypeSource.Map(value).Status);
}
