using Booster.Core.Abstractions;
using Booster.Core.Profiles;

namespace Booster.Core.SystemHealth;

public sealed class StartupItemRestorer(IUserRegistry registry) : UserRegistryRestorer(registry)
{
    public override string ActionId => ActionIds.StartupItem;

    protected override bool IsAllowed(string subKey, string valueName) =>
        valueName.Length > 0
        && (string.Equals(subKey, StartupApprovedKeys.Run, StringComparison.OrdinalIgnoreCase)
            || string.Equals(subKey, StartupApprovedKeys.StartupFolder, StringComparison.OrdinalIgnoreCase));
}
