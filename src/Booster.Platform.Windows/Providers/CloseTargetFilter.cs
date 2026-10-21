using Booster.Core.Profiles;

namespace Booster.Platform.Windows.Providers;

internal static class CloseTargetFilter
{
    public static IReadOnlyList<int> Filter(IReadOnlyList<(string Name, int ProcessId)> candidates, int ownProcessId)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates
            .Where(c => c.ProcessId != ownProcessId && ProcessNames.Normalize(c.Name) is { } name && !ProtectedProcesses.IsProtected(name))
            .Select(c => c.ProcessId)
            .ToArray();
    }
}
