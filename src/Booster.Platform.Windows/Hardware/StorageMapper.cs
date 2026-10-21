using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Hardware;

public static class StorageMapper
{
    private const string PhysicalDisk = "WMI MSFT_PhysicalDisk";
    private const string Partition = "WMI MSFT_Partition";
    private const string DiskDrive = "WMI Win32_DiskDrive";
    private const string LogicalDisk = "WMI Win32_LogicalDisk";
    private const string OrphanNote = "volumes not matched to a physical disk";
    private const string NoLogicalDiskNote = "no Win32_LogicalDisk for this drive letter";
    private const string FallbackNote = @"root\Microsoft\Windows\Storage unavailable";

    // DriveType: 4 — сетевой диск, 5 — оптический привод.
    private static readonly HashSet<int> NonLocalDriveTypes = [4, 5];

    public static IReadOnlyList<StorageDeviceInfo> MapMsft(IReadOnlyList<CimRow> physicalDisks, IReadOnlyList<CimRow> partitions,
        IReadOnlyList<CimRow> logicalDisks)
    {
        ArgumentNullException.ThrowIfNull(physicalDisks);
        ArgumentNullException.ThrowIfNull(partitions);
        ArgumentNullException.ThrowIfNull(logicalDisks);

        var volumes = LocalVolumes(logicalDisks);
        var byLetter = new Dictionary<string, CimRow>(StringComparer.Ordinal);
        foreach (var (letter, row) in volumes)
            byLetter.TryAdd(letter, row);

        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var devices = new List<StorageDeviceInfo>();
        foreach (var disk in physicalDisks)
        {
            var diskVolumes = new List<VolumeInfo>();
            if (long.TryParse(disk.GetString("DeviceId"), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                foreach (var partition in partitions)
                {
                    if (partition.GetInt64("DiskNumber") != number
                        || PartitionLetter(partition.GetString("DriveLetter")) is not { } letter
                        || !claimed.Add(letter))
                        continue;
                    diskVolumes.Add(byLetter.TryGetValue(letter, out var logical)
                        ? Volume(letter, logical)
                        : new VolumeInfo(letter, Reading<string>.Unknown(LogicalDisk, NoLogicalDiskNote),
                            Positive(partition.GetInt64("Size"), $"{Partition}.Size"), Reading<long>.Unknown(LogicalDisk, NoLogicalDiskNote)));
                }
            }

            devices.Add(new StorageDeviceInfo(
                Name(disk, "FriendlyName", PhysicalDisk),
                MapMedia(disk.GetInt32("MediaType")),
                MapBus(disk.GetInt32("BusType")),
                Positive(disk.GetInt64("Size"), $"{PhysicalDisk}.Size"),
                MapHealth(disk.GetInt32("HealthStatus")),
                diskVolumes));
        }

        AddOrphans(devices, volumes, claimed, OrphanNote);
        return devices;
    }

    public static IReadOnlyList<StorageDeviceInfo> MapFallback(IReadOnlyList<CimRow> win32DiskDrive, IReadOnlyList<CimRow> logicalDisks)
    {
        ArgumentNullException.ThrowIfNull(win32DiskDrive);
        ArgumentNullException.ThrowIfNull(logicalDisks);

        var devices = win32DiskDrive
            .OrderBy(d => d.GetInt64("Index") ?? long.MaxValue)
            .Select(d => new StorageDeviceInfo(
                Name(d, "Model", DiskDrive),
                Reading<StorageMediaKind>.Unknown(DiskDrive, FallbackNote),
                Reading<StorageBusKind>.Unknown(DiskDrive, FallbackNote),
                Positive(d.GetInt64("Size"), $"{DiskDrive}.Size"),
                Reading<StorageHealth>.Unknown(DiskDrive, FallbackNote),
                []))
            .ToList();

        AddOrphans(devices, LocalVolumes(logicalDisks), claimed: [], $"{OrphanNote} ({FallbackNote})");
        return devices;
    }

    internal static Reading<StorageMediaKind> MapMedia(int? mediaType)
    {
        const string source = $"{PhysicalDisk}.MediaType";
        return mediaType switch
        {
            3 => Reading<StorageMediaKind>.Known(StorageMediaKind.Hdd, source),
            4 => Reading<StorageMediaKind>.Known(StorageMediaKind.Ssd, source),
            5 => Reading<StorageMediaKind>.Known(StorageMediaKind.Scm, source),
            null or 0 => Reading<StorageMediaKind>.Unknown(source, "media type not reported"),
            _ => Reading<StorageMediaKind>.Unknown(source, $"MediaType {mediaType}"),
        };
    }

    internal static Reading<StorageBusKind> MapBus(int? busType)
    {
        const string source = $"{PhysicalDisk}.BusType";
        StorageBusKind? kind = busType switch
        {
            null or 0 => null,
            17 => StorageBusKind.Nvme,
            11 => StorageBusKind.Sata,
            10 => StorageBusKind.Sas,
            7 => StorageBusKind.Usb,
            8 => StorageBusKind.Raid,
            14 or 15 => StorageBusKind.Virtual,
            _ => StorageBusKind.Other,
        };
        return kind is { } known
            ? Reading<StorageBusKind>.Known(known, source)
            : Reading<StorageBusKind>.Unknown(source, "bus type not reported");
    }

    internal static Reading<StorageHealth> MapHealth(int? healthStatus)
    {
        const string source = $"{PhysicalDisk}.HealthStatus";
        return healthStatus switch
        {
            0 => Reading<StorageHealth>.Known(StorageHealth.Healthy, source),
            1 => Reading<StorageHealth>.Known(StorageHealth.Warning, source),
            2 => Reading<StorageHealth>.Known(StorageHealth.Unhealthy, source),
            5 => Reading<StorageHealth>.Unknown(source, "disk reports its health as Unknown"),
            _ => Reading<StorageHealth>.Unknown(source, healthStatus is null ? null : $"HealthStatus {healthStatus}"),
        };
    }

    private static IReadOnlyList<(string Letter, CimRow Row)> LocalVolumes(IReadOnlyList<CimRow> logicalDisks) =>
        logicalDisks
            .Where(d => d.GetInt32("DriveType") is not { } type || !NonLocalDriveTypes.Contains(type))
            .Select(d => (Letter: LogicalDiskLetter(d.GetString("DeviceID")), Row: d))
            .Where(v => v.Letter is not null)
            .Select(v => (v.Letter!, v.Row))
            .ToArray();

    private static void AddOrphans(List<StorageDeviceInfo> devices, IReadOnlyList<(string Letter, CimRow Row)> volumes,
        HashSet<string> claimed, string note)
    {
        var orphans = volumes
            .Where(v => !claimed.Contains(v.Letter) && v.Row.GetInt64("Size") > 0)
            .Select(v => Volume(v.Letter, v.Row))
            .ToArray();
        if (orphans.Length == 0)
            return;

        devices.Add(new StorageDeviceInfo(
            Reading<string>.Unknown(note: note),
            Reading<StorageMediaKind>.Unknown(note: note),
            Reading<StorageBusKind>.Unknown(note: note),
            Reading<long>.Unknown(note: note),
            Reading<StorageHealth>.Unknown(note: note),
            orphans));
    }

    private static VolumeInfo Volume(string letter, CimRow logical) => new(
        letter,
        logical.GetString("FileSystem") is { } fileSystem
            ? Reading<string>.Known(fileSystem, $"{LogicalDisk}.FileSystem")
            : Reading<string>.Unknown($"{LogicalDisk}.FileSystem", "no file system reported"),
        Positive(logical.GetInt64("Size"), $"{LogicalDisk}.Size"),
        logical.GetInt64("FreeSpace") is >= 0 and var free
            ? Reading<long>.Known(free, $"{LogicalDisk}.FreeSpace")
            : Reading<long>.Unknown($"{LogicalDisk}.FreeSpace"));

    private static Reading<string> Name(CimRow row, string property, string source) =>
        OemPlaceholders.Clean(row.GetString(property)) is { } name
            ? Reading<string>.Known(name, $"{source}.{property}")
            : Reading<string>.Unknown($"{source}.{property}");

    private static Reading<long> Positive(long? value, string source) =>
        value is > 0 ? Reading<long>.Known(value.Value, source) : Reading<long>.Unknown(source);

    private static string? PartitionLetter(string? text) =>
        text is { Length: 1 } && char.IsAsciiLetter(text[0]) ? $"{char.ToUpperInvariant(text[0])}:" : null;

    private static string? LogicalDiskLetter(string? text) =>
        text is { Length: 2 } && char.IsAsciiLetter(text[0]) && text[1] == ':' ? $"{char.ToUpperInvariant(text[0])}:" : null;
}
