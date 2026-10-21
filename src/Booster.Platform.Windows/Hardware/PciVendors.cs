using System.Globalization;
using System.Text.RegularExpressions;
using Booster.Core.Inventory;

namespace Booster.Platform.Windows.Hardware;

public static partial class PciVendors
{
    public const uint Nvidia = 0x10DE;
    public const uint Amd = 0x1002;
    public const uint AmdChipset = 0x1022;
    public const uint Intel = 0x8086;
    public const uint Microsoft = 0x1414;

    public static GpuVendor FromId(uint vendorId) => vendorId switch
    {
        Nvidia => GpuVendor.Nvidia,
        Amd or AmdChipset => GpuVendor.Amd,
        Intel => GpuVendor.Intel,
        Microsoft => GpuVendor.Microsoft,
        _ => GpuVendor.Other,
    };

    /// <summary>Разбирает <c>PCI\VEN_xxxx&amp;DEV_xxxx[&amp;SUBSYS_xxxxxxxx][&amp;REV_xx]\…</c>.</summary>
    public static bool TryParsePnpId(string? pnpDeviceId, out PciId id)
    {
        id = default;
        if (string.IsNullOrEmpty(pnpDeviceId))
            return false;

        var match = PnpPattern().Match(pnpDeviceId);
        if (!match.Success)
            return false;

        id = new PciId(
            ushort.Parse(match.Groups["ven"].ValueSpan, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
            ushort.Parse(match.Groups["dev"].ValueSpan, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
            match.Groups["subsys"].Success
                ? uint.Parse(match.Groups["subsys"].ValueSpan, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)
                : 0,
            match.Groups["rev"].Success
                ? byte.Parse(match.Groups["rev"].ValueSpan, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)
                : (byte)0);
        return true;
    }

    [GeneratedRegex(
        @"^PCI\\VEN_(?<ven>[0-9A-F]{4})&DEV_(?<dev>[0-9A-F]{4})(&SUBSYS_(?<subsys>[0-9A-F]{8}))?(&REV_(?<rev>[0-9A-F]{2}))?(\\|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PnpPattern();
}
