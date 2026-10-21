using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Recovery;
using Booster.Core.Resources;

namespace Booster.Core.Profiles.Actions;

/// <summary>
/// Возврат приоритета процессу из снимка — по PID и времени старта. Процесса нет или PID достался другому
/// процессу — <see cref="ObservedState.Gone"/>: менять нечего, чужой процесс не трогается. Процесс с этим PID и
/// именем работает, но приоритет не читается — исключение: сказать, что возвращать нечего, нельзя.
/// </summary>
public sealed class GamePriorityRestorer(IProcessPriorityController priorities, IProcessProvider processes) : IActionRestorer
{
    public string ActionId => ActionIds.GamePriority;

    public int SnapshotSchemaVersion => GamePriorityValue.SchemaVersion;

    public Task<ObservedState> InspectAsync(ActionSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ct.ThrowIfCancellationRequested();

        var original = GamePriorityValue.Read(snapshot.OriginalValue);
        ProcessPriority? applied = snapshot.AppliedValue is { } value ? GamePriorityValue.Read(value).Priority : null;
        return Task.FromResult(priorities.GetPriority(original.Identity) switch
        {
            null when IsListed(original) => throw new InvalidOperationException(
                string.Format(CultureInfo.CurrentCulture, ReportStrings.Restore_PriorityUnreadableFormat, original.Name)),
            null => ObservedState.Gone,
            var current when current == original.Priority => ObservedState.Original,
            var current when current == applied => ObservedState.Applied,
            _ => ObservedState.Other,
        });
    }

    public Task RevertAsync(ActionSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ct.ThrowIfCancellationRequested();

        var original = GamePriorityValue.Read(snapshot.OriginalValue);
        priorities.SetPriority(original.Identity, original.Priority);
        return Task.CompletedTask;
    }

    // Список процессов строится без открытия процесса: в нём есть и те, к которым нет доступа, а вышедших нет.
    private bool IsListed(GamePriorityValue original) =>
        processes.List().Any(p => p.ProcessId == original.ProcessId
            && string.Equals(ProcessNames.Normalize(p.Name), original.Name, StringComparison.OrdinalIgnoreCase));
}
