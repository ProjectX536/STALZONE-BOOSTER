using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Drivers;

public static class DriverInventoryMapper
{
    private const string Entity = "WMI Win32_PnPEntity";
    private const string SignedDriver = "WMI Win32_PnPSignedDriver";
    private const string ErrorCodeSource = $"{Entity}.ConfigManagerErrorCode";
    private const string NoDriverRowNote = "device has no row in Win32_PnPSignedDriver";

    // CM_PROB_DISABLED: отключено пользователем или политикой, это не сбой драйвера.
    private const int DisabledCode = 22;

    public static DriverInventoryResult Map(IReadOnlyList<CimRow> signedDrivers, IReadOnlyList<CimRow> pnpEntities)
    {
        ArgumentNullException.ThrowIfNull(signedDrivers);
        ArgumentNullException.ThrowIfNull(pnpEntities);

        var drivers = new Dictionary<string, CimRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in signedDrivers)
        {
            if (row.GetString("DeviceID") is { } id)
                drivers.TryAdd(id, row);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var devices = new List<DeviceDriverInfo>();
        foreach (var entity in pnpEntities)
        {
            if (entity.GetString("DeviceID") is not { } id || entity.GetBoolean("Present") == false || !seen.Add(id))
                continue;
            devices.Add(Device(id, entity, drivers.GetValueOrDefault(id)));
        }

        return new DriverInventoryResult(devices);
    }

    public static Reading<DeviceState> MapState(int? configManagerErrorCode) => configManagerErrorCode switch
    {
        null => Reading<DeviceState>.Unknown(ErrorCodeSource),
        0 => Reading<DeviceState>.Known(DeviceState.Ok, ErrorCodeSource),
        DisabledCode => Reading<DeviceState>.Known(DeviceState.Disabled, ErrorCodeSource),
        _ => Reading<DeviceState>.Known(DeviceState.Error, ErrorCodeSource),
    };

    private static DeviceDriverInfo Device(string id, CimRow entity, CimRow? driver)
    {
        var errorCode = entity.GetInt32("ConfigManagerErrorCode");
        return new DeviceDriverInfo(
            id,
            Text(entity.GetString("Name"), $"{Entity}.Name"),
            Text(entity.GetString("PNPClass"), $"{Entity}.PNPClass"),
            entity.GetStringArray("HardwareID"),
            entity.GetStringArray("CompatibleID"),
            DriverText(driver, "DriverProviderName"),
            DriverText(driver, "DriverVersion"),
            driver is null
                ? NoDriver<DateOnly>("DriverDate")
                : DmtfDate.ParseDate(driver.GetString("DriverDate")) is { } date
                    ? Reading<DateOnly>.Known(date, $"{SignedDriver}.DriverDate")
                    : Reading<DateOnly>.Unknown($"{SignedDriver}.DriverDate"),
            DriverText(driver, "InfName"),
            driver is null
                ? NoDriver<bool>("IsSigned")
                : driver.GetBoolean("IsSigned") is { } signed
                    ? Reading<bool>.Known(signed, $"{SignedDriver}.IsSigned")
                    : Reading<bool>.Unknown($"{SignedDriver}.IsSigned"),
            DriverText(driver, "Signer"),
            MapState(errorCode),
            errorCode is { } code ? Reading<int>.Known(code, ErrorCodeSource) : Reading<int>.Unknown(ErrorCodeSource));
    }

    private static Reading<string> DriverText(CimRow? driver, string property) =>
        driver is null ? NoDriver<string>(property) : Text(driver.GetString(property), $"{SignedDriver}.{property}");

    private static Reading<T> NoDriver<T>(string property) => Reading<T>.Unknown($"{SignedDriver}.{property}", NoDriverRowNote);

    private static Reading<string> Text(string? value, string source) =>
        value is null ? Reading<string>.Unknown(source) : Reading<string>.Known(value, source);
}
