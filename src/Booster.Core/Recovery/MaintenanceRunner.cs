using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Profiles;
using Booster.Core.Reports;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Recovery;

public enum MaintenanceStatus
{
    Applied,

    NotSupported,

    Refused,

    NotApplied,

    Failed,
}

public sealed record MaintenanceResult(MaintenanceStatus Status, SessionRecord? Session, string? Detail);

public sealed partial class MaintenanceRunner(ActionExecutor executor, RollbackManager rollback, RecoveryValidator validator,
    IChangeJournal journal, IPrivilegeInfo privileges, TimeProvider time, ILogger<MaintenanceRunner> logger)
{
    public const ActionMode Mode = ActionMode.AskFirst;

    public const string ProfileId = "maintenance";

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<MaintenanceResult> ApplyAsync(IReversibleAction action, bool userConfirmed, bool gamingSessionActive,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var support = await CheckSupportAsync(action, ct).ConfigureAwait(false);
            if (!support.IsSupported)
            {
                LogNotSupported(action.Id, support.Reason);
                return new MaintenanceResult(MaintenanceStatus.NotSupported, null, support.Reason);
            }

            var now = time.GetUtcNow();
            var session = new SessionRecord(SessionRecord.CurrentSchemaVersion, SessionIds.New(now), ProfileId, "", "", null,
                ExecutionTrigger.UserInitiated, now, SessionState.Apply, null, null, [], [], SessionKind.Maintenance);
            var context = new PolicyContext(ExecutionTrigger.UserInitiated, gamingSessionActive, UserEnabled: false,
                UserConfirmed: userConfirmed, CanElevate: privileges.GetCurrent().IsElevated);
            var result = await executor.ExecuteAsync(session, action, support.Summary, Mode, context, CancellationToken.None)
                .ConfigureAwait(false);

            switch (result.Status)
            {
                case ExecutionStatus.Applied:
                    session = await FinishAsync(result.Session, SessionOutcome.Completed).ConfigureAwait(false);
                    LogApplied(session.SessionId, action.Id);
                    return new MaintenanceResult(MaintenanceStatus.Applied, session, null);

                case ExecutionStatus.Refused:
                    return new MaintenanceResult(MaintenanceStatus.Refused, null, EnumText.Format(result.Decision.Reason));

                case ExecutionStatus.NotApplied:
                    var skipped = new SkippedAction(action.Id, action.Descriptor.DisplayName, result.Detail ?? "");
                    session = await FinishAsync(result.Session with { Skipped = [.. result.Session.Skipped, skipped] },
                        SessionOutcome.Completed).ConfigureAwait(false);
                    return new MaintenanceResult(MaintenanceStatus.NotApplied, session, result.Detail);

                default:
                    var detail = result.Error?.Message;
                    if (result.Session.Changes.Count == 0)
                        return new MaintenanceResult(MaintenanceStatus.Failed, null, detail);

                    LogRollingBack(result.Session.SessionId, action.Id);
                    session = await RestoreAsync(result.Session).ConfigureAwait(false);
                    session = await FinishAsync(session, SessionOutcome.RolledBack).ConfigureAwait(false);
                    return new MaintenanceResult(MaintenanceStatus.Failed, session, detail);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SessionRecord> RevertAsync(string sessionId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var contents = await journal.LoadAsync(ct).ConfigureAwait(false);
            var session = contents.Sessions.FirstOrDefault(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal))
                ?? throw new ArgumentException($"Session '{sessionId}' is not in the journal", nameof(sessionId));
            if (session.Kind != SessionKind.Maintenance)
                throw new ArgumentException($"Session '{sessionId}' is not a maintenance session", nameof(sessionId));
            if (!session.Changes.Any(c => c.Status is ChangeStatus.Pending or ChangeStatus.Applied or ChangeStatus.RestoreFailed))
                return session;

            LogReverting(sessionId);
            session = await RestoreAsync(session).ConfigureAwait(false);
            session = session with { State = SessionState.Idle };
            await SaveQuietlyAsync(session).ConfigureAwait(false);
            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ActionSupport> CheckSupportAsync(IReversibleAction action, CancellationToken ct)
    {
        try
        {
            return await action.CheckSupportAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogSupportCheckFailed(ex, action.Id);
            return new ActionSupport(false, ex.Message, action.Descriptor.DisplayName);
        }
    }

    private async Task<SessionRecord> RestoreAsync(SessionRecord session)
    {
        session = session with { State = SessionState.Restore };
        await SaveQuietlyAsync(session).ConfigureAwait(false);
        session = await rollback.RestoreAsync(session, CancellationToken.None).ConfigureAwait(false);
        return await validator.VerifyAsync(session with { State = SessionState.Verify }, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<SessionRecord> FinishAsync(SessionRecord session, SessionOutcome outcome)
    {
        session = session with { State = SessionState.Idle, Outcome = outcome, FinishedAt = time.GetUtcNow() };
        await SaveQuietlyAsync(session).ConfigureAwait(false);
        return session;
    }

    private async Task SaveQuietlyAsync(SessionRecord session)
    {
        try
        {
            await journal.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogSaveFailed(ex, session.SessionId, session.State);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Maintenance action {ActionId} not supported: {Reason}")]
    private partial void LogNotSupported(string actionId, string? reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Support check of maintenance action {ActionId} failed")]
    private partial void LogSupportCheckFailed(Exception exception, string actionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Maintenance session {SessionId}: {ActionId} applied")]
    private partial void LogApplied(string sessionId, string actionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Maintenance session {SessionId}: {ActionId} failed, rolling back")]
    private partial void LogRollingBack(string sessionId, string actionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Maintenance session {SessionId} is being reverted by the user")]
    private partial void LogReverting(string sessionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Journal of maintenance session {SessionId} not saved in state {State}")]
    private partial void LogSaveFailed(Exception exception, string sessionId, SessionState state);
}
