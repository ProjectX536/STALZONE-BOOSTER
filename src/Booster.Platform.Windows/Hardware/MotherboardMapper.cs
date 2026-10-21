using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Hardware;

public static class MotherboardMapper
{
    private const string PcSystemTypeSource = "WMI Win32_ComputerSystem.PCSystemType";
    private const string ChassisSource = "WMI Win32_SystemEnclosure.ChassisTypes";

    // SMBIOS System Enclosure Type (таблица 7.4.1): портативные и стационарные корпуса.
    private static readonly HashSet<int> LaptopChassis = [8, 9, 10, 14, 31, 32];
    private static readonly HashSet<int> DesktopChassis = [3, 4, 5, 6, 7, 13, 15, 16, 35, 36];

    public static SystemBoardInfo Map(CimRow? computerSystem, CimRow? baseBoard, CimRow? bios, CimRow? enclosure,
        Reading<FirmwareKind> firmware) => new(
        Text(computerSystem, "Win32_ComputerSystem", "Manufacturer"),
        Text(computerSystem, "Win32_ComputerSystem", "Model"),
        MapFormFactor(computerSystem?.GetInt32("PCSystemType"), enclosure?.GetInt32Array("ChassisTypes") ?? []),
        Text(baseBoard, "Win32_BaseBoard", "Manufacturer"),
        Text(baseBoard, "Win32_BaseBoard", "Product"),
        Text(bios, "Win32_BIOS", "Manufacturer"),
        Text(bios, "Win32_BIOS", "SMBIOSBIOSVersion"),
        DmtfDate.ParseDate(bios?.GetString("ReleaseDate")) is { } date
            ? Reading<DateOnly>.Known(date, "WMI Win32_BIOS.ReleaseDate")
            : Reading<DateOnly>.Unknown("WMI Win32_BIOS.ReleaseDate"),
        firmware);

    /// <param name="pcSystemType">Win32_ComputerSystem.PCSystemType: 1 Desktop, 2 Mobile, 3 Workstation.</param>
    /// <param name="chassisTypes">Win32_SystemEnclosure.ChassisTypes — используются, когда PCSystemType не решает.</param>
    public static Reading<PcFormFactor> MapFormFactor(int? pcSystemType, IReadOnlyList<int> chassisTypes)
    {
        switch (pcSystemType)
        {
            case 1 or 3:
                return Reading<PcFormFactor>.Known(PcFormFactor.Desktop, PcSystemTypeSource);
            case 2:
                return Reading<PcFormFactor>.Known(PcFormFactor.Laptop, PcSystemTypeSource);
        }

        var laptop = chassisTypes.Any(LaptopChassis.Contains);
        var desktop = chassisTypes.Any(DesktopChassis.Contains);
        return (laptop, desktop) switch
        {
            (true, false) => Reading<PcFormFactor>.Known(PcFormFactor.Laptop, ChassisSource),
            (false, true) => Reading<PcFormFactor>.Known(PcFormFactor.Desktop, ChassisSource),
            (true, true) => Reading<PcFormFactor>.Unknown(ChassisSource, "chassis types contradict each other"),
            _ => Reading<PcFormFactor>.Unknown($"{PcSystemTypeSource}, {ChassisSource}"),
        };
    }

    private static Reading<string> Text(CimRow? row, string wmiClass, string property) =>
        OemPlaceholders.Clean(row?.GetString(property)) is { } value
            ? Reading<string>.Known(value, $"WMI {wmiClass}.{property}")
            : Reading<string>.Unknown($"WMI {wmiClass}.{property}");
}
