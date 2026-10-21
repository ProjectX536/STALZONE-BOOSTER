using Booster.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Recovery;

/// <param name="Sessions">От новых к старым.</param>
public sealed record JournalContents(IReadOnlyList<SessionRecord> Sessions, IReadOnlyList<JournalProblem> Problems);

public interface IChangeJournal
{
    /// <summary>Переписывает <c>&lt;SessionId&gt;.json</c> целиком.</summary>
    Task SaveAsync(SessionRecord session, CancellationToken ct);

    Task<JournalContents> LoadAsync(CancellationToken ct);

    /// <summary>
    /// Удаляет завершённые сессии сверх <paramref name="retainedFinished"/> последних. Незавершённые и проблемные файлы не трогает;
    /// сессии с изменением в статусе Applied или RestoreFailed не удаляет и не считает: без их снимка изменение нельзя вернуть.
    /// </summary>
    Task PruneAsync(int retainedFinished, CancellationToken ct);
}

/// <summary>Журнал сессий: файл на сессию (docs/ARCHITECTURE.md, раздел 7).</summary>
public sealed partial class ChangeJournal(IJournalFileStore files, ILogger<ChangeJournal> logger) : IChangeJournal
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task SaveAsync(SessionRecord session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!SessionIds.IsValid(session.SessionId))
            throw new ArgumentException($"'{session.SessionId}' is not a session id", nameof(session));

        var json = SnapshotStore.Serialize(session);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await files.WriteAsync(FileName(session), json, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Под той же блокировкой, что запись: файл сессии не читается посреди её замены.</summary>
    public async Task<JournalContents> LoadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<JournalContents> LoadCoreAsync(CancellationToken ct)
    {
        var sessions = new List<SessionRecord>();
        var problems = new List<JournalProblem>();
        foreach (var name in files.List())
        {
            ct.ThrowIfCancellationRequested();
            string json;
            try
            {
                json = await files.ReadAsync(name, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add(new JournalProblem(name, JournalProblemKind.Unreadable, ex.Message));
                LogProblem(name, JournalProblemKind.Unreadable, ex.Message);
                continue;
            }

            var read = SnapshotStore.Deserialize(name, json);
            if (read.Session is not null)
            {
                sessions.Add(read.Session);
            }
            else if (read.Problem is not null)
            {
                problems.Add(read.Problem);
                LogProblem(name, read.Problem.Kind, read.Problem.Detail);
            }
        }

        return new JournalContents(
            sessions.OrderByDescending(s => s.StartedAt).ThenByDescending(s => s.SessionId, StringComparer.Ordinal).ToArray(),
            problems);
    }

    public async Task PruneAsync(int retainedFinished, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retainedFinished);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var contents = await LoadCoreAsync(ct).ConfigureAwait(false);
            // RestoreFailed — значение Booster всё ещё в системе, возврат можно повторить: без снимка это станет невозможно.
            var prunable = contents.Sessions.Where(s => s.IsFinished
                && !s.Changes.Any(c => c.Status is ChangeStatus.Applied or ChangeStatus.RestoreFailed));
            foreach (var session in prunable.Skip(retainedFinished))
            {
                try
                {
                    files.Delete(FileName(session));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LogDeleteFailed(ex, FileName(session));
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static string FileName(SessionRecord session) => session.SessionId + ".json";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Journal file {FileName} skipped: {Kind}, {Detail}")]
    private partial void LogProblem(string fileName, JournalProblemKind kind, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot delete old journal file {FileName}")]
    private partial void LogDeleteFailed(Exception exception, string fileName);
}
