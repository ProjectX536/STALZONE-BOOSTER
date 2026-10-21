using System.Security.Principal;
using Booster.Core.Abstractions;
using Booster.Core.Inventory;

namespace Booster.Platform.Windows.Common;

public static class PrivilegeEvaluator
{
    /// <summary>BUILTIN\Performance Log Users: членам группы разрешены ETW-сессии без повышения.</summary>
    public const string PerformanceLogUsersSid = "S-1-5-32-559";

    public static PrivilegeState Evaluate(bool isElevated, IEnumerable<string> groupSids)
    {
        ArgumentNullException.ThrowIfNull(groupSids);
        return new(isElevated, groupSids.Contains(PerformanceLogUsersSid, StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Права текущего токена. «Повышен» — группа Administrators в токене включена: именно это ETW проверяет
/// при запуске сессии. У ограниченного токена UAC эта группа только запрещающая (deny-only), а
/// <see cref="WindowsIdentity.Groups"/> возвращает лишь включённые группы.
/// </summary>
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
