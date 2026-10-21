using Booster.Core.Recovery;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Tests.Recovery;

public class ChangeJournalTests
{
    private readonly InMemoryJournalFileStore _files = new();
    private readonly ListLogger<ChangeJournal> _log = new();

    private ChangeJournal Journal() => new(_files, _log);

    [Fact]
    public async Task Save_writes_one_file_per_session_and_replaces_it()
    {
        var journal = Journal();
        var session = SessionFactory.Session(changes: [SessionFactory.Change("a")]);

        await journal.SaveAsync(session, TestContext.Current.CancellationToken);
        await journal.SaveAsync(session with { State = SessionState.Restore }, TestContext.Current.CancellationToken);

        var file = Assert.Single(_files.Files);
        Assert.Equal($"{SessionFactory.Id}.json", file.Key);
        Assert.Contains("\"state\": \"Restore\"", file.Value);
    }

    [Fact]
    public async Task Load_returns_sessions_newest_first_and_reports_broken_files()
    {
        var journal = Journal();
        var older = SessionFactory.Session("20261018-120000-00000001", startedAt: SessionFactory.Start.AddDays(-1));
        var newer = SessionFactory.Session("20261019-120000-00000002");
        await journal.SaveAsync(newer, TestContext.Current.CancellationToken);
        await journal.SaveAsync(older, TestContext.Current.CancellationToken);
        _files.Put("20261017-120000-00000003.json", "{ broken");

        var contents = await journal.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["20261019-120000-00000002", "20261018-120000-00000001"], contents.Sessions.Select(s => s.SessionId));
        var problem = Assert.Single(contents.Problems);
        Assert.Equal("20261017-120000-00000003.json", problem.FileName);
        Assert.Equal(JournalProblemKind.InvalidJson, problem.Kind);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Unreadable_file_is_a_problem_not_a_failure()
    {
        var journal = Journal();
        await journal.SaveAsync(SessionFactory.Session(), TestContext.Current.CancellationToken);
        _files.Put("20261017-120000-00000003.json", "{}");
        _files.Unreadable.Add("20261017-120000-00000003.json");

        var contents = await journal.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Single(contents.Sessions);
        Assert.Equal(JournalProblemKind.Unreadable, Assert.Single(contents.Problems).Kind);
    }

    [Fact]
    public async Task Load_waits_for_a_running_save()
    {
        var journal = Journal();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _files.WriteGate = gate;

        var save = journal.SaveAsync(SessionFactory.Session(), TestContext.Current.CancellationToken);
        var load = journal.LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(load.IsCompleted);
        gate.SetResult();
        await save;
        Assert.Equal(SessionFactory.Id, Assert.Single((await load).Sessions).SessionId);
    }

    [Fact]
    public async Task Prune_deletes_only_oldest_finished_sessions()
    {
        var journal = Journal();
        for (var day = 1; day <= 4; day++)
            await journal.SaveAsync(SessionFactory.Finished($"2026100{day}-120000-0000000{day}",
                new DateTimeOffset(2026, 10, day, 12, 0, 0, TimeSpan.Zero)), TestContext.Current.CancellationToken);
        var unfinished = SessionFactory.Session("20260901-120000-00000009", startedAt: new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
        await journal.SaveAsync(unfinished, TestContext.Current.CancellationToken);
        _files.Put("20260801-120000-00000008.json", "{ broken");

        await journal.PruneAsync(2, TestContext.Current.CancellationToken);

        Assert.Equal(["20261001-120000-00000001.json", "20261002-120000-00000002.json"], _files.Deleted.Order());
        Assert.Equal(4, _files.Files.Count);
        Assert.Contains("20260901-120000-00000009.json", _files.Files.Keys);
        Assert.Contains("20260801-120000-00000008.json", _files.Files.Keys);
    }

    [Fact]
    public async Task Prune_keeps_sessions_with_applied_changes()
    {
        var journal = Journal();
        for (var day = 1; day <= 3; day++)
            await journal.SaveAsync(SessionFactory.Finished($"2026100{day}-120000-0000000{day}",
                new DateTimeOffset(2026, 10, day, 12, 0, 0, TimeSpan.Zero)), TestContext.Current.CancellationToken);
        var maintenance = SessionFactory.Finished("20260901-120000-00000009", new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero))
            with { Kind = SessionKind.Maintenance, Changes = [SessionFactory.Change("a")] };
        await journal.SaveAsync(maintenance, TestContext.Current.CancellationToken);

        await journal.PruneAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(["20261001-120000-00000001.json", "20261002-120000-00000002.json"], _files.Deleted.Order());
        Assert.Equal(["20260901-120000-00000009.json", "20261003-120000-00000003.json"], _files.Files.Keys.Order());
    }

    [Fact]
    public async Task Prune_keeps_sessions_with_failed_restore()
    {
        var journal = Journal();
        for (var day = 1; day <= 2; day++)
            await journal.SaveAsync(SessionFactory.Finished($"2026100{day}-120000-0000000{day}",
                new DateTimeOffset(2026, 10, day, 12, 0, 0, TimeSpan.Zero)), TestContext.Current.CancellationToken);
        var maintenance = SessionFactory.Finished("20260901-120000-00000009", new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero))
            with { Kind = SessionKind.Maintenance, Changes = [SessionFactory.Change("a", status: ChangeStatus.RestoreFailed)] };
        await journal.SaveAsync(maintenance, TestContext.Current.CancellationToken);

        await journal.PruneAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(["20261001-120000-00000001.json"], _files.Deleted);
        Assert.Contains("20260901-120000-00000009.json", _files.Files.Keys);
    }

    [Theory]
    [InlineData(@"..\evil")]
    [InlineData("")]
    public async Task Invalid_session_id_is_rejected_before_writing(string id)
    {
        var journal = Journal();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            journal.SaveAsync(SessionFactory.Session(id), TestContext.Current.CancellationToken));
        Assert.Equal(0, _files.Writes);
    }
}
