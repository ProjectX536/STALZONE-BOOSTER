using System.Globalization;

namespace Booster.Core.Inventory;

/// <summary>Идентификаторы PCI из <c>PCI\VEN_xxxx&amp;DEV_xxxx&amp;SUBSYS_xxxxxxxx&amp;REV_xx</c>.</summary>
public readonly record struct PciId(ushort VendorId, ushort DeviceId, uint SubsystemId, byte Revision)
{
    /// <summary>Hardware ID без префикса <c>PCI\</c>: так его показывают диспетчер устройств и сайты производителей.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"VEN_{VendorId:X4}&DEV_{DeviceId:X4}&SUBSYS_{SubsystemId:X8}&REV_{Revision:X2}");
}
