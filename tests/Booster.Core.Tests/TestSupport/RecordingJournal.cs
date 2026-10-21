using Booster.Core.Recovery;

namespace Booster.Core.Tests.TestSupport;

public sealed class RecordingJournal : IChangeJournal
{
    private readonly List<SessionRecord> _saves = [];

    public Func<SessionRecord, Exception?>? FailWith { get; set; }

    public Exception? LoadFailure { get; set; }

    public IReadOnlyList<SessionRecord> Saves
    {
        get { lock (_saves) return _saves.ToArray(); }
    }

    public SessionRecord? Last
    {
        get { lock (_saves) return _saves.Count == 0 ? null : _saves[^1]; }
    }

    public Task SaveAsync(SessionRecord session, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (FailWith?.Invoke(session) is { } error)
            throw error;
        lock (_saves) _saves.Add(session);
        return Task.CompletedTask;
    }

    public Task<JournalContents> LoadAsync(CancellationToken ct)
    {
        if (LoadFailure is { } failure)
            return Task.FromException<JournalContents>(failure);
        lock (_saves)
        {
            var latest = _saves.GroupBy(s => s.SessionId).Select(g => g.Last()).OrderByDescending(s => s.StartedAt).ToArray();
            return Task.FromResult(new JournalContents(latest, []));
        }
    }

    public Task PruneAsync(int retainedFinished, CancellationToken ct) => Task.CompletedTask;
}
