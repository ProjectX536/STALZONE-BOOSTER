using System.Text.RegularExpressions;
using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Hardware;

public static partial class RamMapper
{
    private const string Wmi = "WMI Win32_PhysicalMemory";

    // Win32_PhysicalMemoryArray.Use: 3 — системная память (остальное — видеопамять, flash, кэш).
    private const int UseSystemMemory = 3;

    public static MemoryInventory Map(IReadOnlyList<CimRow> physicalMemory, IReadOnlyList<CimRow> memoryArray,
        CimRow? computerSystem)
    {
        var modules = physicalMemory.Select(MapModule).ToList();

        var slots = memoryArray
            .Where(a => a.GetInt32("Use") is null or UseSystemMemory)
            .Sum(a => a.GetInt32("MemoryDevices") is > 0 and var n ? n : 0);

        return new MemoryInventory(
            computerSystem?.GetInt64("TotalPhysicalMemory") is > 0 and var total
                ? Reading<long>.Known(total, "WMI Win32_ComputerSystem.TotalPhysicalMemory")
                : Reading<long>.Unknown("WMI Win32_ComputerSystem.TotalPhysicalMemory"),
            modules,
            slots > 0
                ? Reading<int>.Known(slots, "WMI Win32_PhysicalMemoryArray.MemoryDevices")
                : Reading<int>.Unknown("WMI Win32_PhysicalMemoryArray.MemoryDevices"),
            // Windows не сообщает, в каком режиме реально работает контроллер памяти; метки слотов — только подсказка.
            Reading<bool>.Unknown(note: "Windows does not report the active memory channel mode"),
            ChannelHint(physicalMemory));
    }

    /// <summary>SMBIOS Memory Device Type (таблица 7.18.2).</summary>
    public static Reading<string> MapMemoryType(int? smbiosMemoryType) => smbiosMemoryType switch
    {
        24 => Reading<string>.Known("DDR3", $"{Wmi}.SMBIOSMemoryType"),
        26 => Reading<string>.Known("DDR4", $"{Wmi}.SMBIOSMemoryType"),
        30 => Reading<string>.Known("LPDDR4", $"{Wmi}.SMBIOSMemoryType"),
        34 => Reading<string>.Known("DDR5", $"{Wmi}.SMBIOSMemoryType"),
        35 => Reading<string>.Known("LPDDR5", $"{Wmi}.SMBIOSMemoryType"),
        _ => Reading<string>.Unknown($"{Wmi}.SMBIOSMemoryType",
            smbiosMemoryType is { } v ? $"SMBIOS type {v}" : null),
    };

    private static MemoryModule MapModule(CimRow row) => new(
        row.GetInt64("Capacity") is > 0 and var capacity
            ? Reading<long>.Known(capacity, $"{Wmi}.Capacity")
            : Reading<long>.Unknown($"{Wmi}.Capacity"),
        MapMemoryType(row.GetInt32("SMBIOSMemoryType")),
        Speed(row, "Speed"),
        Speed(row, "ConfiguredClockSpeed"),
        Text(row, "Manufacturer"),
        Text(row, "PartNumber"),
        Text(row, "DeviceLocator"),
        Text(row, "BankLabel"));

    // SMBIOS хранит скорость в МТ/с; 0 означает «неизвестно».
    private static Reading<int> Speed(CimRow row, string property) =>
        row.GetInt32(property) is > 0 and var mts
            ? Reading<int>.Known(mts, $"{Wmi}.{property}")
            : Reading<int>.Unknown($"{Wmi}.{property}");

    private static Reading<string> Text(CimRow row, string property) =>
        OemPlaceholders.Clean(row.GetString(property)) is { } value
            ? Reading<string>.Known(value, $"{Wmi}.{property}")
            : Reading<string>.Unknown($"{Wmi}.{property}");

    private static ChannelLabelHint ChannelHint(IReadOnlyList<CimRow> modules)
    {
        if (modules.Count == 0)
            return ChannelLabelHint.None;

        var keys = new List<string>(modules.Count);
        foreach (var module in modules)
        {
            var locator = module.GetString("DeviceLocator");
            var bank = module.GetString("BankLabel");
            var letter = ChannelLetter(locator) ?? ChannelLetter(bank);
            var controller = Controller(locator) ?? Controller(bank);
            // Модуль без распознанного канала делает подсказку недостоверной для всего набора.
            if (letter is null && controller is null)
                return ChannelLabelHint.None;
            keys.Add($"{controller}|{letter}");
        }

        return keys.Distinct(StringComparer.Ordinal).Count() > 1
            ? ChannelLabelHint.DifferentChannelsByLabel
            : ChannelLabelHint.SameChannelByLabel;
    }

    private static string? ChannelLetter(string? label)
    {
        if (label is null)
            return null;
        var match = ChannelPattern().Match(label);
        if (!match.Success)
            match = DimmPattern().Match(label);
        if (!match.Success)
            match = SlotPattern().Match(label.Trim());
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    private static string? Controller(string? label) =>
        label is not null && ControllerPattern().Match(label) is { Success: true } match ? match.Groups[1].Value : null;

    // "ChannelA-DIMM0", "P0 CHANNEL A".
    [GeneratedRegex(@"Channel\s?([A-H])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChannelPattern();

    // "DIMMA1", "DIMM_B1".
    [GeneratedRegex(@"DIMM_?([A-H])\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DimmPattern();

    // "A1", "B2".
    [GeneratedRegex(@"^([A-H])\d$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SlotPattern();

    // "Controller0-DIMMA1": номер контроллера памяти считается отдельным каналом.
    [GeneratedRegex(@"Controller(\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ControllerPattern();
}
