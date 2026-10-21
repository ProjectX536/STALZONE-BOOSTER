using Booster.Core.Abstractions;
using Booster.Core.Resources;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Recovery;

public sealed partial class RollbackManager(IEnumerable<IActionRestorer> restorers, IChangeJournal journal, TimeProvider time,
    ILogger<RollbackManager> logger)
{
    private readonly Dictionary<string, IActionRestorer> _restorers = restorers.ToDictionary(r => r.ActionId, StringComparer.Ordinal);

    public async Task<SessionRecord> RestoreAsync(SessionRecord session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);

        var changes = session.Changes.ToArray();
        for (var i = changes.Length - 1; i >= 0; i--)
        {
            var change = changes[i];
            if (change.Status is not (ChangeStatus.Pending or ChangeStatus.Applied or ChangeStatus.RestoreFailed))
                continue;

            ct.ThrowIfCancellationRequested();
            var (status, message) = await RestoreOneAsync(change, ct).ConfigureAwait(false);
            var carried = change.Status == ChangeStatus.RestoreFailed ? null : change.Detail;
            changes[i] = change with { Status = status, Detail = RecoveryText.Join(carried, message), UpdatedAt = time.GetUtcNow() };
            LogRestored(change.ActionId, status);

            session = session with { Changes = changes.ToArray() };
            await SaveAsync(session, ct).ConfigureAwait(false);
        }

        return session;
    }

    private async Task<(ChangeStatus Status, string? Message)> RestoreOneAsync(ChangeEntry change, CancellationToken ct)
    {
        if (!change.IsReversible)
            return (ChangeStatus.NeedsManualCheck, ReportStrings.Restore_Irreversible);
        if (!_restorers.TryGetValue(change.ActionId, out var restorer))
            return (ChangeStatus.NeedsManualCheck, ReportStrings.Restore_NoRestorer);
        if (change.Snapshot.SchemaVersion > restorer.SnapshotSchemaVersion)
            return (ChangeStatus.NeedsManualCheck, ReportStrings.Restore_NewerSnapshot);

        try
        {
            switch (await restorer.InspectAsync(change.Snapshot, ct).ConfigureAwait(false))
            {
                case ObservedState.Original or ObservedState.Gone:
                    return (ChangeStatus.Restored, null);
                case ObservedState.Other:
                    return (ChangeStatus.RestoreConflict, null);
            }

            await restorer.RevertAsync(change.Snapshot, ct).ConfigureAwait(false);
            return await restorer.InspectAsync(change.Snapshot, ct).ConfigureAwait(false) is ObservedState.Original or ObservedState.Gone
                ? (change.RequiresReboot ? ChangeStatus.NeedsReboot : ChangeStatus.Restored, null)
                : (ChangeStatus.RestoreFailed, ReportStrings.Restore_StillNotOriginal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogRestoreFailed(ex, change.ActionId);
            return (ChangeStatus.RestoreFailed, ex.Message);
        }
    }

    private async Task SaveAsync(SessionRecord session, CancellationToken ct)
    {
        try
        {
            await journal.SaveAsync(session, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogSaveFailed(ex, session.SessionId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Restore of {ActionId}: {Status}")]
    private partial void LogRestored(string actionId, ChangeStatus status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Restore of {ActionId} failed")]
    private partial void LogRestoreFailed(Exception exception, string actionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Journal of session {SessionId} not saved during restore")]
    private partial void LogSaveFailed(Exception exception, string sessionId);
}

internal static class RecoveryText
{
    public static string? Join(string? first, string? second) =>
        string.IsNullOrEmpty(first) ? second
        : string.IsNullOrEmpty(second) ? first
        : $"{first}; {second}";
}
