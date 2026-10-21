using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Drivers;

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
