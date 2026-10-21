using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;
using Booster.Platform.Windows.Hardware;
using Booster.Platform.Windows.Tests.TestSupport;

namespace Booster.Platform.Windows.Tests.Hardware;

public class RamMapperTests
{
    private static readonly CimRow ComputerSystem = WmiFixture.Row(("TotalPhysicalMemory", 17013911552L));

    private static CimRow Module(string locator, string bank = "BANK 0", long type = 26, long configured = 3200) =>
        WmiFixture.Row(("Capacity", 8589934592L), ("SMBIOSMemoryType", type), ("ConfiguredClockSpeed", configured),
            ("Speed", 3200L), ("DeviceLocator", locator), ("BankLabel", bank));

    [Fact]
    public void Real_machine_has_two_ddr4_modules_on_different_channels()
    {
        var memory = RamMapper.Map(
            WmiFixture.Load("desktop-nvidia/Win32_PhysicalMemory.json"),
            WmiFixture.Load("desktop-nvidia/Win32_PhysicalMemoryArray.json"),
            WmiFixture.LoadSingle("desktop-nvidia/Win32_ComputerSystem.json"));

        Assert.Equal(2, memory.Modules.Count);
        Assert.All(memory.Modules, m =>
        {
            Assert.Equal(8_589_934_592L, m.CapacityBytes.Value);
            Assert.Equal("DDR4", m.MemoryType.Value);
            Assert.Equal(2667, m.ConfiguredSpeedMts.Value);
            Assert.Equal(2667, m.RatedSpeedMts.Value);
            Assert.Equal("Patriot Memory (PDP Systems)", m.Manufacturer.Value);
            Assert.Equal("PSD48G266681", m.PartNumber.Value);
        });
        Assert.Equal(["Controller0-DIMMA1", "Controller1-DIMMB1"], memory.Modules.Select(m => m.Locator.Value));
        Assert.Equal(ChannelLabelHint.DifferentChannelsByLabel, memory.ChannelHint);
        Assert.Equal(17_013_911_552L, memory.TotalPhysicalBytes.Value);
        Assert.Equal("WMI Win32_ComputerSystem.TotalPhysicalMemory", memory.TotalPhysicalBytes.Source);
        Assert.Equal(2, memory.SlotCount.Value);
    }

    [Fact]
    public void Slot_count_comes_from_system_memory_arrays()
    {
        var arrays = new[]
        {
            WmiFixture.Row(("MemoryDevices", 2L), ("Use", 3L)),
            WmiFixture.Row(("MemoryDevices", 2L), ("Use", 3L)),
            WmiFixture.Row(("MemoryDevices", 1L), ("Use", 4L)),
        };

        var memory = RamMapper.Map([], arrays, ComputerSystem);

        Assert.Equal(4, memory.SlotCount.Value);
        Assert.Equal("WMI Win32_PhysicalMemoryArray.MemoryDevices", memory.SlotCount.Source);
    }

    [Fact]
    public void Missing_or_zero_slot_count_is_unknown()
    {
        Assert.Equal(DataStatus.Unknown, RamMapper.Map([], [], ComputerSystem).SlotCount.Status);
        Assert.Equal(DataStatus.Unknown,
            RamMapper.Map([], [WmiFixture.Row(("MemoryDevices", 0L), ("Use", 3L))], ComputerSystem).SlotCount.Status);
    }

    [Fact]
    public void Zero_configured_speed_is_unknown()
    {
        var module = Assert.Single(RamMapper.Map([Module("ChannelA-DIMM0", configured: 0)], [], ComputerSystem).Modules);

        Assert.Equal(DataStatus.Unknown, module.ConfiguredSpeedMts.Status);
        Assert.Equal(3200, module.RatedSpeedMts.Value);
    }

    [Theory]
    [InlineData(24, "DDR3")]
    [InlineData(26, "DDR4")]
    [InlineData(30, "LPDDR4")]
    [InlineData(34, "DDR5")]
    [InlineData(35, "LPDDR5")]
    public void Smbios_memory_type_is_named(int smbios, string expected)
    {
        var type = RamMapper.MapMemoryType(smbios);

        Assert.Equal(expected, type.Value);
        Assert.Equal("WMI Win32_PhysicalMemory.SMBIOSMemoryType", type.Source);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    [InlineData(null)]
    public void Zero_or_unlisted_memory_type_is_unknown(int? smbios) =>
        Assert.Equal(DataStatus.Unknown, RamMapper.MapMemoryType(smbios).Status);

    [Fact]
    public void Dual_channel_is_always_unknown()
    {
        var real = RamMapper.Map(WmiFixture.Load("desktop-nvidia/Win32_PhysicalMemory.json"), [], ComputerSystem);

        Assert.Equal(DataStatus.Unknown, real.DualChannel.Status);
        Assert.NotNull(real.DualChannel.Note);
        Assert.Equal(DataStatus.Unknown, RamMapper.Map([], [], null).DualChannel.Status);
    }

    [Fact]
    public void Channel_letters_in_labels_give_different_channels()
    {
        var memory = RamMapper.Map([Module("ChannelA-DIMM1"), Module("ChannelB-DIMM1")], [], ComputerSystem);

        Assert.Equal(ChannelLabelHint.DifferentChannelsByLabel, memory.ChannelHint);
    }

    [Fact]
    public void Two_modules_in_one_channel_give_same_channel()
    {
        var memory = RamMapper.Map([Module("ChannelA-DIMM0"), Module("ChannelA-DIMM1")], [], ComputerSystem);

        Assert.Equal(ChannelLabelHint.SameChannelByLabel, memory.ChannelHint);
    }

    [Theory]
    [InlineData("DIMM_A1", "DIMM_B1", ChannelLabelHint.DifferentChannelsByLabel)]
    [InlineData("dimma1", "dimma2", ChannelLabelHint.SameChannelByLabel)]
    [InlineData("A1", "B1", ChannelLabelHint.DifferentChannelsByLabel)]
    [InlineData("Controller0-DIMM0", "Controller1-DIMM0", ChannelLabelHint.DifferentChannelsByLabel)]
    [InlineData("DIMM 0", "DIMM 1", ChannelLabelHint.None)]
    [InlineData("ChannelA-DIMM0", "DIMM 1", ChannelLabelHint.None)]
    public void Channel_is_read_from_label_patterns(string first, string second, ChannelLabelHint expected) =>
        Assert.Equal(expected, RamMapper.Map([Module(first), Module(second)], [], ComputerSystem).ChannelHint);

    [Fact]
    public void Controller_and_channel_together_tell_modules_apart()
    {
        var memory = RamMapper.Map(
            WmiFixture.Load("laptop-hybrid.synthetic/Win32_PhysicalMemory.json"), [],
            WmiFixture.LoadSingle("laptop-hybrid.synthetic/Win32_ComputerSystem.json"));

        Assert.Equal(ChannelLabelHint.DifferentChannelsByLabel, memory.ChannelHint);
        Assert.All(memory.Modules, m => Assert.Equal("DDR5", m.MemoryType.Value));
    }

    [Fact]
    public void Bank_label_is_used_when_locator_has_no_channel()
    {
        // Так подписывают слоты платы ASRock и Gigabyte: "DIMM 0" + "P0 CHANNEL A".
        var memory = RamMapper.Map([Module("DIMM 0", bank: "P0 CHANNEL A"), Module("DIMM 1", bank: "P0 CHANNEL B")], [],
            ComputerSystem);

        Assert.Equal(ChannelLabelHint.DifferentChannelsByLabel, memory.ChannelHint);

        memory = RamMapper.Map([Module("DIMM 0", bank: "ChannelA"), Module("DIMM 1", bank: "ChannelA")], [], ComputerSystem);

        Assert.Equal(ChannelLabelHint.SameChannelByLabel, memory.ChannelHint);
    }

    [Fact]
    public void Placeholder_manufacturer_is_unknown()
    {
        var memory = RamMapper.Map(WmiFixture.Load("laptop-hybrid.synthetic/Win32_PhysicalMemory.json"), [], null);

        Assert.All(memory.Modules, m =>
        {
            Assert.Equal(DataStatus.Unknown, m.Manufacturer.Status);
            Assert.Equal("EXAMPLE-DDR5-16G", m.PartNumber.Value);
        });
    }

    [Fact]
    public void Empty_physical_memory_keeps_total_from_computer_system()
    {
        var memory = RamMapper.Map([], [], ComputerSystem);

        Assert.Empty(memory.Modules);
        Assert.Equal(17_013_911_552L, memory.TotalPhysicalBytes.Value);
        Assert.Equal(ChannelLabelHint.None, memory.ChannelHint);
    }

    [Fact]
    public void Missing_computer_system_leaves_total_unknown()
    {
        Assert.Equal(DataStatus.Unknown, RamMapper.Map([], [], null).TotalPhysicalBytes.Status);
    }
}
