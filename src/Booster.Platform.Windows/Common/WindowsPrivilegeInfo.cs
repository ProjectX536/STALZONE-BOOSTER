using System.Security.Principal;
using Booster.Core.Abstractions;
using Booster.Core.Inventory;

namespace Booster.Platform.Windows.Common;

public static class PrivilegeEvaluator
{
    public const string PerformanceLogUsersSid = "S-1-5-32-559";

    public static PrivilegeState Evaluate(bool isElevated, IEnumerable<string> groupSids)
    {
        ArgumentNullException.ThrowIfNull(groupSids);
        return new(isElevated, groupSids.Contains(PerformanceLogUsersSid, StringComparer.OrdinalIgnoreCase));
    }
}

public sealed class WindowsPrivilegeInfo : IPrivilegeInfo
{
    public PrivilegeState GetCurrent()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var isElevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        var groups = identity.Groups?.Select(g => g.Value) ?? [];
        return PrivilegeEvaluator.Evaluate(isElevated, groups);
    }
}
