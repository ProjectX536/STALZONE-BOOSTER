using Booster.Core.Inventory;

namespace Booster.Core.Abstractions;

public static class CapabilityScopes
{
    public const string System = "system";

    public static string Adapter(AdapterLuid luid) => $"adapter:{luid}";
}

public interface ICapabilityRegistry
{
    void Report(CapabilityReport report);

    CapabilityReport Get(string capabilityId, string scope = CapabilityScopes.System);

    IReadOnlyList<CapabilityReport> All();
}
