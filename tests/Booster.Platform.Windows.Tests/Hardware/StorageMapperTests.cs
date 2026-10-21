using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;
using Booster.Platform.Windows.Hardware;
using Booster.Platform.Windows.Tests.TestSupport;

namespace Booster.Platform.Windows.Tests.Hardware;

public class StorageMapperTests
{
    private static IReadOnlyList<CimRow> Load(string name) => WmiFixture.Load($"desktop-nvidia/{name}.json");

    private static IReadOnlyList<StorageDeviceInfo> RealMachine() =>
        StorageMapper.MapMsft(Load("MSFT_PhysicalDisk"), Load("MSFT_Partition"), Load("Win32_LogicalDisk"));

    private static CimRow Disk(string deviceId, long mediaType = 4, long busType = 17, long health = 0) =>
        WmiFixture.Row(("DeviceId", deviceId), ("FriendlyName", "Disk " + deviceId), ("MediaType", mediaType),
            ("BusType", busType), ("Size", 1_000_204_886_016L), ("HealthStatus", health));

    [Fact]
    public void Real_machine_has_sata_and_nvme_ssds_with_volumes()
    {
        var disks = RealMachine();

        Assert.Equal(2, disks.Count);
        var sata = disks.Single(d => d.FriendlyName.Value == "SPCC Solid State Disk");
        var nvme = disks.Single(d => d.FriendlyName.Value == "Patriot M.2 P300 512GB");
        Assert.Equal(StorageMediaKind.Ssd, sata.Media.Value);
        Assert.Equal(StorageBusKind.Sata, sata.Bus.Value);
        Assert.Equal(StorageBusKind.Nvme, nvme.Bus.Value);
        Assert.Equal(StorageHealth.Healthy, nvme.Health.Value);
        Assert.Equal(512_110_190_592L, nvme.SizeBytes.Value);
        Assert.Equal(["D:"], sata.Volumes.Select(v => v.DriveLetter));
        Assert.Equal(["C:", "E:"], nvme.Volumes.Select(v => v.DriveLetter));

        var c = nvme.Volumes[0];
        Assert.Equal("NTFS", c.FileSystem.Value);
        Assert.Equal(213_088_317_440L, c.SizeBytes.Value);
        Assert.True(c.FreeBytes.Value <= c.SizeBytes.Value);
        Assert.Equal("WMI Win32_LogicalDisk.FreeSpace", c.FreeBytes.Source);
    }

    [Theory]
    [InlineData(3, StorageMediaKind.Hdd)]
    [InlineData(4, StorageMediaKind.Ssd)]
    [InlineData(5, StorageMediaKind.Scm)]
    public void Media_type_is_mapped(long mediaType, StorageMediaKind expected) =>
        Assert.Equal(expected, Assert.Single(StorageMapper.MapMsft([Disk("0", mediaType: mediaType)], [], [])).Media.Value);

    [Fact]
    public void Media_type_zero_is_unknown() =>
        Assert.Equal(DataStatus.Unknown, Assert.Single(StorageMapper.MapMsft([Disk("0", mediaType: 0)], [], [])).Media.Status);

    [Theory]
    [InlineData(17, StorageBusKind.Nvme)]
    [InlineData(11, StorageBusKind.Sata)]
    [InlineData(10, StorageBusKind.Sas)]
    [InlineData(7, StorageBusKind.Usb)]
    [InlineData(8, StorageBusKind.Raid)]
    [InlineData(14, StorageBusKind.Virtual)]
    [InlineData(15, StorageBusKind.Virtual)]
    [InlineData(99, StorageBusKind.Other)]
    public void Bus_type_is_mapped(long busType, StorageBusKind expected) =>
        Assert.Equal(expected, Assert.Single(StorageMapper.MapMsft([Disk("0", busType: busType)], [], [])).Bus.Value);

    [Fact]
    public void Bus_type_zero_is_unknown() =>
        Assert.Equal(DataStatus.Unknown, Assert.Single(StorageMapper.MapMsft([Disk("0", busType: 0)], [], [])).Bus.Status);

    [Theory]
    [InlineData(0, StorageHealth.Healthy)]
    [InlineData(1, StorageHealth.Warning)]
    [InlineData(2, StorageHealth.Unhealthy)]
    public void Health_is_mapped(long health, StorageHealth expected) =>
        Assert.Equal(expected, Assert.Single(StorageMapper.MapMsft([Disk("0", health: health)], [], [])).Health.Value);

    [Fact]
    public void Health_five_is_unknown() =>
        Assert.Equal(DataStatus.Unknown, Assert.Single(StorageMapper.MapMsft([Disk("0", health: 5)], [], [])).Health.Status);

    [Fact]
    public void Partition_without_letter_makes_no_volume()
    {
        var partitions = new[] { WmiFixture.Row(("DiskNumber", 0L), ("DriveLetter", null)), WmiFixture.Row(("DiskNumber", 0L)) };

        Assert.Empty(Assert.Single(StorageMapper.MapMsft([Disk("0")], partitions, [])).Volumes);
    }

    [Fact]
    public void Partition_letter_without_logical_disk_keeps_volume_with_unknown_sizes()
    {
        var partitions = new[] { WmiFixture.Row(("DiskNumber", 0L), ("DriveLetter", "F")) };

        var volume = Assert.Single(Assert.Single(StorageMapper.MapMsft([Disk("0")], partitions, [])).Volumes);

        Assert.Equal("F:", volume.DriveLetter);
        Assert.Equal(DataStatus.Unknown, volume.FreeBytes.Status);
    }

    [Fact]
    public void Logical_disk_without_partition_goes_to_unknown_device()
    {
        var logical = Load("Win32_LogicalDisk").Append(WmiFixture.Row(("DeviceID", "Z:"), ("DriveType", 3L),
            ("FileSystem", "exFAT"), ("Size", 64_000_000_000L), ("FreeSpace", 1_000_000L))).ToList();

        var disks = StorageMapper.MapMsft(Load("MSFT_PhysicalDisk"), Load("MSFT_Partition"), logical);

        var unknown = Assert.Single(disks, d => !d.FriendlyName.IsAvailable);
        Assert.Equal(DataStatus.Unknown, unknown.FriendlyName.Status);
        Assert.Equal(DataStatus.Unknown, unknown.Media.Status);
        Assert.Equal("Z:", Assert.Single(unknown.Volumes).DriveLetter);
        Assert.Equal(3, disks.Count);
    }

    [Fact]
    public void Network_optical_and_empty_drives_are_not_volumes()
    {
        var logical = new[]
        {
            WmiFixture.Row(("DeviceID", "N:"), ("DriveType", 4L), ("FileSystem", "NTFS"), ("Size", 1L), ("FreeSpace", 1L)),
            WmiFixture.Row(("DeviceID", "R:"), ("DriveType", 5L), ("FileSystem", "UDF"), ("Size", 1L), ("FreeSpace", 0L)),
            WmiFixture.Row(("DeviceID", "G:"), ("DriveType", 2L)),
        };

        Assert.Empty(StorageMapper.MapMsft([], [], logical));
    }

    [Fact]
    public void Disk_with_non_numeric_device_id_gets_no_volumes()
    {
        var disk = Disk("{5B0D2A3C-0000-0000-0000-000000000000}");
        var partitions = new[] { WmiFixture.Row(("DiskNumber", 0L), ("DriveLetter", "C")) };
        var logical = new[] { WmiFixture.Row(("DeviceID", "C:"), ("DriveType", 3L), ("Size", 10L), ("FreeSpace", 5L)) };

        var disks = StorageMapper.MapMsft([disk], partitions, logical);

        Assert.Empty(disks.Single(d => d.FriendlyName.IsAvailable).Volumes);
        Assert.Equal("C:", Assert.Single(disks.Single(d => !d.FriendlyName.IsAvailable).Volumes).DriveLetter);
    }

    [Fact]
    public void Fallback_uses_disk_drive_size_and_leaves_media_and_bus_unknown()
    {
        var disks = StorageMapper.MapFallback(Load("Win32_DiskDrive"), Load("Win32_LogicalDisk"));

        var spcc = disks.Single(d => d.FriendlyName.Value == "SPCC Solid State Disk");
        Assert.Equal(512_105_932_800L, spcc.SizeBytes.Value);
        Assert.Equal("WMI Win32_DiskDrive.Size", spcc.SizeBytes.Source);
        Assert.Equal(DataStatus.Unknown, spcc.Media.Status);
        Assert.Equal(DataStatus.Unknown, spcc.Bus.Status);
        Assert.Equal(DataStatus.Unknown, spcc.Health.Status);
        Assert.Equal(["C:", "D:", "E:"], disks.Single(d => !d.FriendlyName.IsAvailable).Volumes.Select(v => v.DriveLetter));
    }
}
