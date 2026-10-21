namespace Booster.Core.Configuration;

public sealed record VendorLinkRule(string Match, string Title, string Url);

public sealed record OfficialSources(
    int SchemaVersion,
    IReadOnlyList<string> AllowedHosts,
    IReadOnlyList<VendorLinkRule> GpuVendors,
    IReadOnlyList<VendorLinkRule> BoardVendors,
    string WindowsUpdateUri)
{
    public const int CurrentSchemaVersion = 1;
}
