using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Recovery;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Profiles;

public enum ExecutionStatus
{
    Applied,
    Refused,
    Failed,

    NotApplied,
}

public sealed record ExecutionResult(ExecutionStatus Status, PolicyDecision Decision, SessionRecord Session, Exception? Error,
    string? Detail = null);

public sealed partial class ActionExecutor(IActionPolicy policy, IChangeJournal journal, TimeProvider time, ILogger<ActionExecutor> logger)
{
    public async Task<ExecutionResult> ExecuteAsync(SessionRecord session, IReversibleAction action, string summary,
        ActionMode? mode, PolicyContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(action);

        var decision = policy.Evaluate(action.Descriptor, mode, context);
        if (decision.Verdict != PolicyVerdict.Allow)
        {
            LogRefused(action.Id, decision.Verdict, decision.Reason);
            return new ExecutionResult(ExecutionStatus.Refused, decision, session, null);
        }

        ActionSnapshot snapshot;
        try
        {
            snapshot = await action.CaptureAsync(ct).ConfigureAwait(false);
            if (!string.Equals(snapshot.ActionId, action.Id, StringComparison.Ordinal))
                throw new InvalidOperationException($"Action '{action.Id}' returned a snapshot of '{snapshot.ActionId}'");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogFailed(ex, action.Id, "capture");
            return new ExecutionResult(ExecutionStatus.Failed, decision, session, ex);
        }

        var descriptor = action.Descriptor;
        var entry = new ChangeEntry(action.Id, descriptor.DisplayName, summary, descriptor.IsReversible, descriptor.RequiresReboot,
            ChangeStatus.Pending, snapshot, null, null, null, time.GetUtcNow());
        var pending = session with { Changes = [.. session.Changes, entry] };
        try
        {
            await journal.SaveAsync(pending, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogFailed(ex, action.Id, "journal");
            return new ExecutionResult(ExecutionStatus.Failed, decision, session, ex);
        }

        ct.ThrowIfCancellationRequested();
        try
        {
            var applied = await action.ApplyAsync(ct).ConfigureAwait(false);
            if (!applied.Applied)
            {
                // Ничего не изменилось: убираем Pending, иначе восстановление искало бы несделанное изменение.
                LogNotApplied(action.Id, applied.Detail);
                await SaveAfterApplyAsync(session, ct).ConfigureAwait(false);
                return new ExecutionResult(ExecutionStatus.NotApplied, decision, session, null, applied.Detail);
            }

            var done = Replace(pending, entry with
            {
                Status = ChangeStatus.Applied,
                Result = applied.Data,
                Detail = applied.Detail,
                UpdatedAt = time.GetUtcNow(),
            });
            await SaveAfterApplyAsync(done, ct).ConfigureAwait(false);
            LogApplied(action.Id);
            return new ExecutionResult(ExecutionStatus.Applied, decision, done, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogFailed(ex, action.Id, "apply");
            var failed = Replace(pending, entry with { Detail = ex.Message, UpdatedAt = time.GetUtcNow() });
            await SaveAfterApplyAsync(failed, ct).ConfigureAwait(false);
            return new ExecutionResult(ExecutionStatus.Failed, decision, failed, ex);
        }
    }

    private static SessionRecord Replace(SessionRecord session, ChangeEntry last) =>
        session with { Changes = [.. session.Changes.Take(session.Changes.Count - 1), last] };

    private async Task SaveAfterApplyAsync(SessionRecord session, CancellationToken ct)
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Action {ActionId} refused: {Verdict}, {Reason}")]
    private partial void LogRefused(string actionId, PolicyVerdict verdict, PolicyReason reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Action {ActionId} applied")]
    private partial void LogApplied(string actionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Action {ActionId} not applied: {Reason}")]
    private partial void LogNotApplied(string actionId, string? reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Action {ActionId} failed at {Step}")]
    private partial void LogFailed(Exception exception, string actionId, string step);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Journal of session {SessionId} not saved after apply")]
    private partial void LogSaveFailed(Exception exception, string sessionId);
}
