using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Profiles;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Recovery;

/// <summary>Изменение незавершённой сессии, которое могло остаться в системе.</summary>
/// <param name="Observed">Текущее состояние; null — проверить нельзя (действие необратимо, нет восстановителя, ошибка чтения).</param>
public sealed record RecoveryItem(string ActionId, string DisplayName, string Summary, ChangeStatus Status, ObservedState? Observed);

/// <summary>Незавершённая сессия, восстановление которой ждёт кнопки «Восстановить».</summary>
public sealed record PendingRecovery(SessionRecord Session, IReadOnlyList<RecoveryItem> Items);

/// <param name="Problems">Файлы журнала, которые нельзя прочитать; они не удаляются.</param>
public sealed record RecoveryReport(IReadOnlyList<SessionRecord> Recovered, IReadOnlyList<PendingRecovery> AwaitingUser,
    IReadOnlyList<JournalProblem> Problems)
{
    public bool IsEmpty => Recovered.Count == 0 && AwaitingUser.Count == 0 && Problems.Count == 0;
}

/// <summary>
/// Восстановление незавершённых сессий при старте (docs/ARCHITECTURE.md, раздел 6, «Окна сбоя»): Recovery →
/// <see cref="RollbackManager"/> → Verify → итог <see cref="SessionOutcome.Recovered"/>. Повторный проход идемпотентен.
/// </summary>
public sealed partial class StartupRecovery(IChangeJournal journal, RollbackManager rollback, RecoveryValidator validator,
    IEnumerable<IActionRestorer> restorers, IProfileRepository profiles, IBoosterConfiguration config, TimeProvider time,
    ILogger<StartupRecovery> logger)
{
    private readonly Dictionary<string, IActionRestorer> _restorers = restorers.ToDictionary(r => r.ActionId, StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Незавершённые сессии: подготовка, обслуживание или recoverInterruptedSession профиля сессии (профиля нет — true) → восстановить;
    /// иначе — в AwaitingUser с текущим состоянием изменений. Затем старые завершённые сессии удаляются.
    /// </summary>
    public async Task<RecoveryReport> RunAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var contents = await journal.LoadAsync(ct).ConfigureAwait(false);
            var recovered = new List<SessionRecord>();
            var awaiting = new List<PendingRecovery>();
            var problems = new List<JournalProblem>(contents.Problems);

            // От новых к старым, как откат внутри сессии: поздняя сессия могла заново изменить то, что оставила ранняя.
            // Сбой одной сессии не мешает восстановить остальные; она остаётся незавершённой до следующего запуска.
            foreach (var session in contents.Sessions.Where(s => !s.IsFinished))
            {
                try
                {
                    // Подготовка содержит только необратимое: возвращать нечего, её завершение лишь помечает изменения для ручной проверки.
                    // Незавершённое обслуживание — сбой посреди применения: пользователь не видел итога, профиля у сессии нет.
                    if (session.Kind is SessionKind.Preparation or SessionKind.Maintenance
                        || (profiles.Current.Find(session.ProfileId)?.Recovery.RecoverInterruptedSession ?? true))
                    {
                        recovered.Add(await RecoverSessionAsync(session, ct).ConfigureAwait(false));
                    }
                    else
                    {
                        LogAwaitingUser(session.SessionId);
                        awaiting.Add(new PendingRecovery(session, await InspectAsync(session, ct).ConfigureAwait(false)));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    LogSessionFailed(ex, session.SessionId);
                    problems.Add(new JournalProblem(ChangeJournal.FileName(session), JournalProblemKind.RecoveryFailed, ex.Message));
                }
            }

            await PruneAsync(ct).ConfigureAwait(false);
            return new RecoveryReport(recovered, awaiting, problems);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Кнопка «Восстановить» для сессии из AwaitingUser. Уже завершённая сессия возвращается как есть.</summary>
    public async Task<SessionRecord> RecoverAsync(string sessionId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var contents = await journal.LoadAsync(ct).ConfigureAwait(false);
            var session = contents.Sessions.FirstOrDefault(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal))
                ?? throw new ArgumentException($"Session '{sessionId}' is not in the journal", nameof(sessionId));
            return session.IsFinished ? session : await RecoverSessionAsync(session, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Сбой записи журнала не мешает вернуть систему: сессия останется незавершённой, и следующий старт
    // повторит проход — восстановление идемпотентно.
    private async Task<SessionRecord> RecoverSessionAsync(SessionRecord session, CancellationToken ct)
    {
        LogRecovering(session.SessionId, session.State);
        session = session with { State = SessionState.Recovery };
        await SaveAsync(session, ct).ConfigureAwait(false);

        session = await rollback.RestoreAsync(session, ct).ConfigureAwait(false);
        session = await validator.VerifyAsync(session with { State = SessionState.Verify }, ct).ConfigureAwait(false);

        session = session with { State = SessionState.Idle, Outcome = SessionOutcome.Recovered, FinishedAt = time.GetUtcNow() };
        await SaveAsync(session, ct).ConfigureAwait(false);
        return session;
    }

    private async Task<IReadOnlyList<RecoveryItem>> InspectAsync(SessionRecord session, CancellationToken ct)
    {
        var items = new List<RecoveryItem>();
        foreach (var change in session.Changes)
        {
            if (change.Status is not (ChangeStatus.Pending or ChangeStatus.Applied or ChangeStatus.RestoreFailed))
                continue;
            var observed = await ObserveAsync(change, ct).ConfigureAwait(false);
            items.Add(new RecoveryItem(change.ActionId, change.DisplayName, change.Summary, change.Status, observed));
        }

        return items;
    }

    private async Task<ObservedState?> ObserveAsync(ChangeEntry change, CancellationToken ct)
    {
        if (!change.IsReversible || !_restorers.TryGetValue(change.ActionId, out var restorer)
            || change.Snapshot.SchemaVersion > restorer.SnapshotSchemaVersion)
            return null;

        try
        {
            return await restorer.InspectAsync(change.Snapshot, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogInspectFailed(ex, change.ActionId);
            return null;
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
            LogSaveFailed(ex, session.SessionId, session.State);
        }
    }

    private async Task PruneAsync(CancellationToken ct)
    {
        try
        {
            await journal.PruneAsync(config.Current.Settings.Journal.RetainedSessionCount, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogPruneFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unfinished session {SessionId} (state {State}) is being recovered")]
    private partial void LogRecovering(string sessionId, SessionState state);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unfinished session {SessionId} waits for the user: recovery is off in its profile")]
    private partial void LogAwaitingUser(string sessionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unfinished session {SessionId} was not recovered")]
    private partial void LogSessionFailed(Exception exception, string sessionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot inspect {ActionId} of an unfinished session")]
    private partial void LogInspectFailed(Exception exception, string actionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Journal of session {SessionId} not saved in state {State} during recovery")]
    private partial void LogSaveFailed(Exception exception, string sessionId, SessionState state);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Old journal sessions were not pruned")]
    private partial void LogPruneFailed(Exception exception);
}
