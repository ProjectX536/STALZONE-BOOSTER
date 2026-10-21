using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Profiles;

namespace Booster.Core.SystemHealth;

public sealed class RegistryTweakRestorer(IUserRegistry registry, IBoosterConfiguration config) : UserRegistryRestorer(registry)
{
    public override string ActionId => ActionIds.RegistryTweak;

    protected override bool IsAllowed(string subKey, string valueName) =>
        config.Current.Catalog.RegistryTweaks.Any(t =>
            string.Equals(t.SubKey, subKey, StringComparison.OrdinalIgnoreCase)
            && (t.ValueName is null ? valueName.Length > 0 : string.Equals(t.ValueName, valueName, StringComparison.OrdinalIgnoreCase)));
}
