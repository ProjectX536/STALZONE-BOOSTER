using Booster.Core.Abstractions;

namespace Booster.Core.Inventory;

public enum DeviceState
{
    Ok,
    Error,
    Disabled,
}

/// <summary>
/// Устройство и его драйвер. Устройство без установленного драйвера (код 28) тоже попадает в список:
/// поля драйвера у него Unknown. <see cref="ProblemCode"/> — ConfigManagerErrorCode.
/// </summary>
public sealed record DeviceDriverInfo(
    string DeviceInstanceId,
    Reading<string> DeviceName,
    Reading<string> DeviceClass,
    IReadOnlyList<string> HardwareIds,
    IReadOnlyList<string> CompatibleIds,
    Reading<string> DriverProvider,
    Reading<string> DriverVersion,
    Reading<DateOnly> DriverDate,
    Reading<string> InfName,
    Reading<bool> IsSigned,
    Reading<string> Signer,
    Reading<DeviceState> State,
    Reading<int> ProblemCode);

public sealed record DriverInventoryResult(IReadOnlyList<DeviceDriverInfo> Devices);
