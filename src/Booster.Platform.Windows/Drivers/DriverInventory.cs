using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Drivers;

/// <summary>
/// Драйверы и ошибки устройств из WMI: два запроса только с нужными свойствами, по очереди — чтобы не нагружать
/// WMI двумя тяжёлыми перечислениями сразу. Win32_PnPSignedDriver перечисляется секундами, поэтому у секции
/// свой таймаут в <c>SnapshotCollector</c>. Сбой запроса не перехватывается: его превращает в статус секции <c>SectionRunner</c>.
/// </summary>
public sealed class DriverInventory(ICimClient cim) : IDriverInventoryProvider
{
    private const string EntityQuery =
        "SELECT DeviceID, Name, PNPClass, HardwareID, CompatibleID, ConfigManagerErrorCode, Present FROM Win32_PnPEntity";
    private const string SignedDriverQuery =
        "SELECT DeviceID, DriverProviderName, DriverVersion, DriverDate, InfName, IsSigned, Signer FROM Win32_PnPSignedDriver";

    public async Task<DriverInventoryResult> ScanAsync(CancellationToken cancellationToken)
    {
        var entities = await cim.QueryAsync(WmiNamespaces.CimV2, EntityQuery, cancellationToken).ConfigureAwait(false);
        var drivers = await cim.QueryAsync(WmiNamespaces.CimV2, SignedDriverQuery, cancellationToken).ConfigureAwait(false);
        return DriverInventoryMapper.Map(drivers, entities);
    }
}
