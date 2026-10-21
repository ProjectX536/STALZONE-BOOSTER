using System.Globalization;

namespace Booster.Core.Inventory;

public readonly record struct PciId(ushort VendorId, ushort DeviceId, uint SubsystemId, byte Revision)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"VEN_{VendorId:X4}&DEV_{DeviceId:X4}&SUBSYS_{SubsystemId:X8}&REV_{Revision:X2}");
}
