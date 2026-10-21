using Booster.Core.Abstractions;
using Booster.Core.Resources;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Recovery;

public sealed partial class RecoveryValidator(IEnumerable<IActionRestorer> restorers, IChangeJournal journal, TimeProvider time,
    ILogger<RecoveryValidator> logger)
{
    private readonly Dictionary<string, IActionRestorer> _restorers = restorers.ToDictionary(r => r.ActionId, StringComparer.Ordinal);

    public async Task<SessionRecord> VerifyAsync(SessionRecord session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);

        var changes = session.Changes.ToArray();
        for (var i = 0; i < changes.Length; i++)
        {
            var change = changes[i];
            if (!change.IsReversible || change.Status is not (ChangeStatus.Restored or ChangeStatus.NeedsReboot))
                continue;

            ct.ThrowIfCancellationRequested();
            changes[i] = await VerifyOneAsync(change, ct).ConfigureAwait(false) with { UpdatedAt = time.GetUtcNow() };
        }

        var verified = session with { Changes = changes };
        try
        {
            await journal.SaveAsync(verified, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogSaveFailed(ex, session.SessionId);
        }

        return verified;
    }

    private async Task<ChangeEntry> VerifyOneAsync(ChangeEntry change, CancellationToken ct)
    {
        if (!_restorers.TryGetValue(change.ActionId, out var restorer))
            return Unknown(change, ReportStrings.Verify_NoRestorer);
        if (change.Snapshot.SchemaVersion > restorer.SnapshotSchemaVersion)
            return Unknown(change, ReportStrings.Restore_NewerSnapshot);

        try
        {
            if (await restorer.InspectAsync(change.Snapshot, ct).ConfigureAwait(false) is ObservedState.Original or ObservedState.Gone)
                return change with { Verified = true };

            LogChangedAgain(change.ActionId);
            return change with
            {
                Verified = false,
                Status = ChangeStatus.NeedsManualCheck,
                Detail = RecoveryText.Join(change.Detail, ReportStrings.Verify_ChangedAgain),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogVerifyFailed(ex, change.ActionId);
            return Unknown(change, ex.Message);
        }
    }

    private static ChangeEntry Unknown(ChangeEntry change, string reason) =>
        change with { Verified = null, Detail = RecoveryText.Join(change.Detail, reason) };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Verification of {ActionId}: value is not original again")]
    private partial void LogChangedAgain(string actionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Verification of {ActionId} failed")]
    private partial void LogVerifyFailed(Exception exception, string actionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Journal of session {SessionId} not saved after verification")]
    private partial void LogSaveFailed(Exception exception, string sessionId);
}
