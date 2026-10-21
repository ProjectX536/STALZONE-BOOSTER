using Booster.Core.Profiles;

namespace Booster.Platform.Windows.Providers;

/// <summary>
/// Отбор процессов, которым <see cref="ProcessProvider.RequestCloseAsync"/> отправит WM_CLOSE. Повторяет отбор
/// BackgroundAppsAction на уровне PID: даже при ошибке в Core защищённый процесс и сам Booster не закрываются.
/// </summary>
internal static class CloseTargetFilter
{
    /// <returns>PID без защищённых имён, пустых имён и собственного процесса, в исходном порядке.</returns>
    public static IReadOnlyList<int> Filter(IReadOnlyList<(string Name, int ProcessId)> candidates, int ownProcessId)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates
            .Where(c => c.ProcessId != ownProcessId && ProcessNames.Normalize(c.Name) is { } name && !ProtectedProcesses.IsProtected(name))
            .Select(c => c.ProcessId)
            .ToArray();
    }
}
