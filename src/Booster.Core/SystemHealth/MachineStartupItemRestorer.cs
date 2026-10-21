using Booster.Core.Abstractions;
using Booster.Core.Profiles;

namespace Booster.Core.SystemHealth;

public sealed class MachineStartupItemRestorer(IMachineRegistry registry) : UserRegistryRestorer(registry)
{
    public override string ActionId => ActionIds.MachineStartupItem;

    protected override bool IsAllowed(string subKey, string valueName) =>
        valueName.Length > 0
        && (string.Equals(subKey, StartupApprovedKeys.Run, StringComparison.OrdinalIgnoreCase)
            || string.Equals(subKey, StartupApprovedKeys.Run32, StringComparison.OrdinalIgnoreCase)
            || string.Equals(subKey, StartupApprovedKeys.StartupFolder, StringComparison.OrdinalIgnoreCase));
}
