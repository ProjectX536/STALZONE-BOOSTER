using Booster.Core.Abstractions;

namespace Booster.Core.Inventory;

public enum StorageMediaKind
{
    Hdd,
    Ssd,
    Scm,
}

public enum StorageBusKind
{
    Nvme,
    Sata,
    Sas,
    Usb,
    Raid,
    Virtual,
    Other,
}

public enum StorageHealth
{
    Healthy,
    Warning,
    Unhealthy,
}

public sealed record VolumeInfo(string DriveLetter, Reading<string> FileSystem, Reading<long> SizeBytes, Reading<long> FreeBytes);

public sealed record StorageDeviceInfo(
    Reading<string> FriendlyName,
    Reading<StorageMediaKind> Media,
    Reading<StorageBusKind> Bus,
    Reading<long> SizeBytes,
    Reading<StorageHealth> Health,
    IReadOnlyList<VolumeInfo> Volumes);
