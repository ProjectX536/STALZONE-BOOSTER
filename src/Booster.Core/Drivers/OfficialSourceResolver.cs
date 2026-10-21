using System.Text.RegularExpressions;
using Booster.Core.Configuration;
using Booster.Core.Inventory;
using Booster.Core.Resources;

namespace Booster.Core.Drivers;

public sealed record OfficialLink(string Title, Uri Url);

public sealed partial class OfficialSourceResolver(IBoosterConfiguration config)
{
    private const string DisplayClass = "Display";
    private const string SystemClass = "System";

    public IReadOnlyList<OfficialLink> ForDevice(DeviceDriverInfo device, SystemBoardInfo? board)
    {
        ArgumentNullException.ThrowIfNull(device);

        var sources = config.Current.Sources;
        var links = new List<OfficialLink>(2);
        if (VendorLink(device, board, sources) is { } vendor)
            links.Add(vendor);
        if (Uri.TryCreate(sources.WindowsUpdateUri, UriKind.Absolute, out var windowsUpdate))
            links.Add(new OfficialLink(ReportStrings.WindowsUpdateLinkTitle, windowsUpdate));
        return links;
    }

    public bool IsAllowed(Uri url) => IsAllowed(url, config.Current.Sources);

    private static bool IsAllowed(Uri url, OfficialSources sources)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsAbsoluteUri)
            return false;
        if (Uri.TryCreate(sources.WindowsUpdateUri, UriKind.Absolute, out var windowsUpdate)
            && string.Equals(url.AbsoluteUri, windowsUpdate.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
            return true;

        // Отсекает адреса вида https://www.nvidia.com@evil.io/
        return url.Scheme == Uri.UriSchemeHttps && url.UserInfo.Length == 0 && url.IsDefaultPort
            && sources.AllowedHosts.Any(allowed => IsHostOrSubdomain(url.IdnHost, allowed));
    }

    private static bool IsHostOrSubdomain(string host, string allowed) =>
        !string.IsNullOrWhiteSpace(allowed)
        && (host.Equals(allowed, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith($".{allowed}", StringComparison.OrdinalIgnoreCase));

    private static OfficialLink? VendorLink(DeviceDriverInfo device, SystemBoardInfo? board, OfficialSources sources)
    {
        IEnumerable<VendorLinkRule> rules = [];
        if (IsClass(device, DisplayClass) && PciVendorId(device.HardwareIds) is { } vendorId)
            rules = sources.GpuVendors.Where(r => r.Match.Equals(vendorId, StringComparison.OrdinalIgnoreCase));
        else if (IsClass(device, SystemClass) && board?.BoardManufacturer is { IsAvailable: true, Value: { } manufacturer })
            rules = sources.BoardVendors.Where(r => !string.IsNullOrWhiteSpace(r.Match)
                && manufacturer.Contains(r.Match, StringComparison.OrdinalIgnoreCase));

        foreach (var rule in rules)
        {
            if (Uri.TryCreate(rule.Url, UriKind.Absolute, out var url) && IsAllowed(url, sources))
                return new OfficialLink(rule.Title, url);
        }
        return null;
    }

    private static bool IsClass(DeviceDriverInfo device, string deviceClass) =>
        device.DeviceClass is { IsAvailable: true, Value: { } value } && value.Equals(deviceClass, StringComparison.OrdinalIgnoreCase);

    private static string? PciVendorId(IReadOnlyList<string> hardwareIds) =>
        hardwareIds.Select(id => PciVendor().Match(id)).FirstOrDefault(m => m.Success)?.Groups["ven"].Value;

    [GeneratedRegex(@"^PCI\\VEN_(?<ven>[0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PciVendor();
}
