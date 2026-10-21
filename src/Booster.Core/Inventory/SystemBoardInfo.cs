using Booster.Core.Abstractions;

namespace Booster.Core.Inventory;

public enum PcFormFactor
{
    Desktop,
    Laptop,
    Other,
}

public enum FirmwareKind
{
    Uefi,
    LegacyBios,
}

public sealed record SystemBoardInfo(
    Reading<string> SystemManufacturer,
    Reading<string> SystemModel,
    Reading<PcFormFactor> FormFactor,
    Reading<string> BoardManufacturer,
    Reading<string> BoardProduct,
    Reading<string> BiosVendor,
    Reading<string> BiosVersion,
    Reading<DateOnly> BiosReleaseDate,
    Reading<FirmwareKind> Firmware);
