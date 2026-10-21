namespace Booster.Core.Configuration;

/// <summary>
/// Правило ссылки на сайт производителя. <see cref="Match"/> — PCI Vendor ID для GPU
/// или подстрока имени производителя для системных плат.
/// </summary>
public sealed record VendorLinkRule(string Match, string Title, string Url);

/// <summary>Содержимое <c>official-sources.json</c>: белый список доменов и ссылки на страницы драйверов.</summary>
public sealed record OfficialSources(
    int SchemaVersion,
    IReadOnlyList<string> AllowedHosts,
    IReadOnlyList<VendorLinkRule> GpuVendors,
    IReadOnlyList<VendorLinkRule> BoardVendors,
    string WindowsUpdateUri)
{
    public const int CurrentSchemaVersion = 1;
}
