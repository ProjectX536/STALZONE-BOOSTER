using Booster.Core.Inventory;

namespace Booster.Core.Abstractions;

public static class CapabilityScopes
{
    public const string System = "system";

    public static string Adapter(AdapterLuid luid) => $"adapter:{luid}";
}

/// <summary>Реестр возможностей: что провайдеры смогли или не смогли на этой машине.</summary>
public interface ICapabilityRegistry
{
    void Report(CapabilityReport report);

    /// <summary>Нет записи — статус Unknown, провайдер "none".</summary>
    CapabilityReport Get(string capabilityId, string scope = CapabilityScopes.System);

    /// <summary>Копия записей, упорядоченная по (CapabilityId, Scope).</summary>
    IReadOnlyList<CapabilityReport> All();
}
