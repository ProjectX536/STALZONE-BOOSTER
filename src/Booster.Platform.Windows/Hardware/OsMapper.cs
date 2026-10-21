using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Hardware;

public static class OsMapper
{
    private const string Wmi = "WMI Win32_OperatingSystem";
    public const string RegistryKey = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    /// <param name="displayVersion">Значение реестра DisplayVersion (REG_SZ, например "24H2").</param>
    /// <param name="ubr">Значение реестра UBR (REG_DWORD); значение другого типа не принимается.</param>
    public static OsInfo Map(CimRow? win32Os, object? displayVersion, object? ubr) => new(
        Text(win32Os, "Caption"),
        Text(win32Os, "Version"),
        win32Os?.GetInt32("BuildNumber") is > 0 and var build
            ? Reading<int>.Known(build, $"{Wmi}.BuildNumber")
            : Reading<int>.Unknown($"{Wmi}.BuildNumber"),
        ubr is int value and >= 0 ? Reading<int>.Known(value, $@"{RegistryKey}\UBR") : Reading<int>.Unknown($@"{RegistryKey}\UBR"),
        displayVersion is string s && !string.IsNullOrWhiteSpace(s)
            ? Reading<string>.Known(s.Trim(), $@"{RegistryKey}\DisplayVersion")
            : Reading<string>.Unknown($@"{RegistryKey}\DisplayVersion"),
        // Текст Windows как есть, на языке системы ("64-разрядная").
        Text(win32Os, "OSArchitecture"),
        DmtfDate.ParseDateTime(win32Os?.GetString("LastBootUpTime")) is { } boot
            ? Reading<DateTimeOffset>.Known(boot, $"{Wmi}.LastBootUpTime")
            : Reading<DateTimeOffset>.Unknown($"{Wmi}.LastBootUpTime"));

    private static Reading<string> Text(CimRow? row, string property) =>
        row?.GetString(property) is { } value
            ? Reading<string>.Known(value, $"{Wmi}.{property}")
            : Reading<string>.Unknown($"{Wmi}.{property}");
}
