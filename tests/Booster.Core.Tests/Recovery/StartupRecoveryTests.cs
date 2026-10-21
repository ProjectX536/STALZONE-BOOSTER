using System.Text.Json.Nodes;
using Booster.Core.Abstractions;
using Booster.Core.Profiles;
using Booster.Core.Recovery;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static Booster.Core.Tests.TestSupport.SessionFactory;

namespace Booster.Core.Tests.Recovery;

public class StartupRecoveryTests
{
    private const string OlderId = "20261018-120000-00000001";

    private readonly FakeSystemState _state = new();
    private readonly InMemoryJournalFileStore _files = new();
    private readonly FakeTimeProvider _time = new(Start.AddHours(2));
    private readonly ChangeJournal _journal;
    private readonly FakeRestorer[] _restorers;
    private FakeProfileRepository _profiles = new(ProfileFactory.Profile());
    private int _retained = 50;

    public StartupRecoveryTests()
    {
        _journal = new ChangeJournal(_files, NullLogger<ChangeJournal>.Instance);
        _restorers = new[] { "a", "b", "c" }.Select(id => new FakeRestorer(_state, id)).ToArray();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private StartupRecovery Recovery() => new(
        _journal,
        new RollbackManager(_restorers, _journal, _time, NullLogger<RollbackManager>.Instance),
        new RecoveryValidator(_restorers, _journal, _time, NullLogger<RecoveryValidator>.Instance),
        _restorers,
        _profiles,
        new FakeConfiguration(retainedSessionCount: _retained),
        _time,
        NullLogger<StartupRecovery>.Instance);

    // Файл кладётся мимо журнала: счётчик записей хранилища показывает только работу StartupRecovery.
    private void Put(SessionRecord session) => _files.Put(session.SessionId + ".json", SnapshotStore.Serialize(session));

    private async Task<SessionRecord> Stored(string id) =>
        (await _journal.LoadAsync(Ct)).Sessions.Single(s => s.SessionId == id);

    private static ChangeStatus[] Statuses(SessionRecord session) => session.Changes.Select(c => c.Status).ToArray();

    [Fact]
    public async Task Interrupted_preparation_is_marked_for_manual_check()
    {
        // Восстановление после сбоя в профиле выключено, но подготовке возвращать нечего: кнопки она не ждёт.
        _profiles = new FakeProfileRepository(ProfileFactory.Profile(recovery: new ProfileRecovery(true, true, false, true)));
        _state["a"] = "boost";
        Put(Session(changes: [Change("a", reversible: false)], state: SessionState.Apply) with
        {
            Kind = SessionKind.Preparation,
            GameProcessId = null,
        });

        var report = await Recovery().RunAsync(Ct);

        var recovered = Assert.Single(report.Recovered);
        Assert.Empty(report.AwaitingUser);
        Assert.Equal([ChangeStatus.NeedsManualCheck], Statuses(recovered));
        Assert.Equal("boost", _state["a"]);
        var stored = await Stored(Id);
        Assert.Equal(SessionKind.Preparation, stored.Kind);
        Assert.Equal(SessionOutcome.Recovered, stored.Outcome);
    }

    [Fact]
    public async Task Unfinished_maintenance_session_is_rolled_back_at_startup()
    {
        // Сбой посреди применения: пользователь не видел итога — изменение возвращается без вопроса, даже если в профиле с
        // тем же Id восстановление выключено.
        _profiles = new FakeProfileRepository(ProfileFactory.Profile(recovery: new ProfileRecovery(true, true, false, true)));
        _state["a"] = "boost";
        _state["b"] = "boost";
        Put(Session(changes: [Change("a"), Change("b", status: ChangeStatus.Pending)], state: SessionState.Apply,
            profileId: ProfileFactory.Profile().ProfileId) with { Kind = SessionKind.Maintenance, GameProcessId = null });

        var report = await Recovery().RunAsync(Ct);

        var recovered = Assert.Single(report.Recovered);
        Assert.Empty(report.AwaitingUser);
        Assert.Equal([ChangeStatus.Restored, ChangeStatus.Restored], Statuses(recovered));
        Assert.Equal(["orig", "orig"], new[] { _state["a"], _state["b"] });
        Assert.Equal(SessionKind.Maintenance, (await Stored(Id)).Kind);
    }

    [Fact]
    public async Task Finished_maintenance_session_is_left_applied()
    {
        _state["a"] = "boost";
        Put(Session(changes: [Change("a")], state: SessionState.Idle, outcome: SessionOutcome.Completed) with
        {
            Kind = SessionKind.Maintenance,
        });

        var report = await Recovery().RunAsync(Ct);

        Assert.True(report.IsEmpty);
        Assert.Equal("boost", _state["a"]);
        Assert.Empty(_state.Calls);
        Assert.Equal([ChangeStatus.Applied], Statuses(await Stored(Id)));
    }

    [Fact]
    public async Task Unfinished_session_from_crash_is_restored_at_startup()
    {
        _state["a"] = "boost";
        _state["b"] = "boost";
        Put(Session(changes: [Change("a"), Change("b")]));

        var report = await Recovery().RunAsync(Ct);

        var recovered = Assert.Single(report.Recovered);
        Assert.Equal(Id, recovered.SessionId);
        Assert.Equal(SessionOutcome.Recovered, recovered.Outcome);
        Assert.Equal(SessionState.Idle, recovered.State);
        Assert.Equal(_time.GetUtcNow(), recovered.FinishedAt);
        Assert.All(recovered.Changes, c => Assert.Equal(ChangeStatus.Restored, c.Status));
        Assert.All(recovered.Changes, c => Assert.True(c.Verified));
        Assert.Equal(["revert:b", "revert:a"], _state.Calls);
        Assert.Equal(["orig", "orig"], new[] { _state["a"], _state["b"] });
        Assert.Empty(report.AwaitingUser);
        Assert.Empty(report.Problems);

        var stored = await Stored(Id);
        Assert.Equal(SessionOutcome.Recovered, stored.Outcome);
        Assert.Equal(SessionState.Idle, stored.State);
        Assert.Equal([ChangeStatus.Restored, ChangeStatus.Restored], Statuses(stored));
        Assert.All(stored.Changes, c => Assert.True(c.Verified));
    }

    [Fact]
    public async Task Recovery_state_is_saved_before_the_first_revert()
    {
        _state["a"] = "boost";
        Put(Session(changes: [Change("a")]));
        SessionState? stateAtRevert = null;
        _restorers[0].OnRevert = _ =>
            stateAtRevert = SnapshotStore.Deserialize(Id + ".json", _files.Files[Id + ".json"]).Session!.State;

        await Recovery().RunAsync(Ct);

        Assert.Equal(SessionState.Recovery, stateAtRevert);
    }

    [Fact]
    public async Task Session_file_without_changes_is_closed_without_reverts()
    {
        Put(Session(state: SessionState.Snapshot));

        var report = await Recovery().RunAsync(Ct);

        var recovered = Assert.Single(report.Recovered);
        Assert.Equal(SessionOutcome.Recovered, recovered.Outcome);
        Assert.Empty(_state.Calls);
        Assert.Equal(SessionOutcome.Recovered, (await Stored(Id)).Outcome);
    }

    [Theory]
    [InlineData("orig", ChangeStatus.Restored, false)]
    [InlineData("boost", ChangeStatus.Restored, true)]
    [InlineData("user", ChangeStatus.RestoreConflict, false)]
    public async Task Pending_change_is_resolved_by_its_current_value(string current, ChangeStatus expected, bool reverted)
    {
        _state["a"] = current;
        Put(Session(changes: [Change("a", status: ChangeStatus.Pending)], state: SessionState.Apply));

        var report = await Recovery().RunAsync(Ct);

        Assert.Equal(expected, Assert.Single(Assert.Single(report.Recovered).Changes).Status);
        Assert.Equal(reverted ? new[] { "revert:a" } : [], _state.Calls);
        Assert.Equal(reverted ? "orig" : current, _state["a"]);
    }

    [Fact]
    public async Task Interrupted_restore_reverts_only_what_is_still_applied()
    {
        _state["a"] = "orig";
        _state["b"] = "boost";
        Put(Session(changes: [Change("a", status: ChangeStatus.Restored), Change("b")], state: SessionState.Restore));

        var report = await Recovery().RunAsync(Ct);

        Assert.Equal(["revert:b"], _state.Calls);
        Assert.Equal([ChangeStatus.Restored, ChangeStatus.Restored], Statuses(Assert.Single(report.Recovered)));
    }

    [Fact]
    public async Task Newer_unfinished_session_is_restored_first()
    {
        // Вторая сессия началась поверх незавершённой первой и сменила то же значение ещё раз.
        _state["a"] = "turbo";
        Put(Session(OlderId, [Change("a", "orig", "boost")], startedAt: Start.AddDays(-1)));
        Put(Session(changes: [Change("a", "boost", "turbo")]));

        var report = await Recovery().RunAsync(Ct);

        Assert.Equal([Id, OlderId], report.Recovered.Select(s => s.SessionId));
        Assert.Equal(["revert:a", "revert:a"], _state.Calls);
        Assert.Equal("orig", _state["a"]);
        Assert.All(report.Recovered, s => Assert.Equal(ChangeStatus.Restored, Assert.Single(s.Changes).Status));
    }

    [Fact]
    public async Task Second_startup_finds_nothing_to_do()
    {
        _state["a"] = "boost";
        _state["b"] = "boost";
        Put(Session(changes: [Change("a"), Change("b")]));
        await Recovery().RunAsync(Ct);
        _state.ClearCalls();

        var report = await Recovery().RunAsync(Ct);

        Assert.True(report.IsEmpty);
        Assert.Empty(_state.Calls);
    }

    [Fact]
    public async Task Finished_sessions_are_not_touched()
    {
        _state["a"] = "boost";
        Put(Session(changes: [Change("a", status: ChangeStatus.RestoreConflict)], state: SessionState.Idle,
            outcome: SessionOutcome.Completed));

        var report = await Recovery().RunAsync(Ct);

        Assert.True(report.IsEmpty);
        Assert.Empty(_state.Calls);
        Assert.Equal(0, _files.Writes);
        Assert.Equal("boost", _state["a"]);
    }

    [Fact]
    public async Task Recovery_disabled_in_profile_lists_items_and_waits()
    {
        _profiles = new FakeProfileRepository(ProfileFactory.Profile(recovery: new ProfileRecovery(true, true, false, true)));
        _state["a"] = "boost";
        _state["b"] = "orig";
        Put(Session(changes: [Change("a"), Change("b", status: ChangeStatus.Restored)]));
        var recovery = Recovery();

        var report = await recovery.RunAsync(Ct);

        Assert.Empty(report.Recovered);
        var pending = Assert.Single(report.AwaitingUser);
        Assert.Equal(Id, pending.Session.SessionId);
        Assert.Equal([new RecoveryItem("a", "Действие a", "orig → boost", ChangeStatus.Applied, ObservedState.Applied)], pending.Items);
        Assert.Empty(_state.Calls);
        Assert.Equal(0, _files.Writes);
        Assert.Null((await Stored(Id)).Outcome);

        var recovered = await recovery.RecoverAsync(Id, Ct);

        Assert.Equal(SessionOutcome.Recovered, recovered.Outcome);
        Assert.Equal([ChangeStatus.Restored, ChangeStatus.Restored], Statuses(recovered));
        Assert.Equal(["revert:a"], _state.Calls);
        Assert.Equal("orig", _state["a"]);
        Assert.True((await recovery.RunAsync(Ct)).IsEmpty);
    }

    [Fact]
    public async Task Awaiting_items_that_cannot_be_inspected_have_unknown_state()
    {
        _profiles = new FakeProfileRepository(ProfileFactory.Profile(recovery: new ProfileRecovery(true, true, false, true)));
        _state["a"] = "boost";
        Put(Session(changes: [Change("a", reversible: false), Change("x", status: ChangeStatus.RestoreFailed)]));

        var report = await Recovery().RunAsync(Ct);

        var items = Assert.Single(report.AwaitingUser).Items;
        Assert.Equal([("a", ChangeStatus.Applied), ("x", ChangeStatus.RestoreFailed)], items.Select(i => (i.ActionId, i.Status)));
        Assert.All(items, i => Assert.Null(i.Observed));
        Assert.Equal(0, _restorers[0].Inspections);
    }

    [Fact]
    public async Task Recover_of_a_finished_session_changes_nothing()
    {
        _state["a"] = "boost";
        Put(Session(changes: [Change("a")], state: SessionState.Idle, outcome: SessionOutcome.Completed));

        var session = await Recovery().RecoverAsync(Id, Ct);

        Assert.Equal(SessionOutcome.Completed, session.Outcome);
        Assert.Empty(_state.Calls);
        Assert.Equal(0, _files.Writes);
    }

    [Fact]
    public async Task Recover_of_an_unknown_session_throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Recovery().RecoverAsync(Id, Ct));
    }

    [Fact]
    public async Task Corrupt_or_newer_journal_is_reported_and_others_recovered()
    {
        const string corrupt = "20261017-120000-00000003.json";
        const string newer = "20261017-130000-00000004.json";
        const string newerJson = """{ "schemaVersion": 2, "sessionId": "20261017-130000-00000004" }""";
        _files.Put(corrupt, "{ broken");
        _files.Put(newer, newerJson);
        _state["a"] = "boost";
        Put(Session(changes: [Change("a")]));

        var report = await Recovery().RunAsync(Ct);

        Assert.Equal(Id, Assert.Single(report.Recovered).SessionId);
        Assert.Equal("orig", _state["a"]);
        Assert.Equal(
            [(corrupt, JournalProblemKind.InvalidJson), (newer, JournalProblemKind.NewerSchemaVersion)],
            report.Problems.Select(p => (p.FileName, p.Kind)).Order());
        Assert.Equal("{ broken", _files.Files[corrupt]);
        Assert.Equal(newerJson, _files.Files[newer]);
        Assert.Empty(_files.Deleted);
    }

    [Fact]
    public async Task Missing_profile_defaults_to_automatic_recovery()
    {
        _profiles = new FakeProfileRepository(ProfileFactory.Profile(recovery: new ProfileRecovery(true, true, false, true)));
        _state["a"] = "boost";
        Put(Session(changes: [Change("a")], profileId: "removed-profile"));

        var report = await Recovery().RunAsync(Ct);

        Assert.Equal(Id, Assert.Single(report.Recovered).SessionId);
        Assert.Equal("orig", _state["a"]);
    }

    [Fact]
    public async Task Prune_uses_configured_count()
    {
        _retained = 2;
        for (var day = 1; day <= 3; day++)
            Put(Finished($"2026101{9 - day}-120000-0000000{day}", Start.AddDays(-day)));
        Put(Session(state: SessionState.Snapshot));

        var report = await Recovery().RunAsync(Ct);

        // Восстановленная сессия уже завершена и считается в число хранимых.
        Assert.Single(report.Recovered);
        Assert.Equal(["20261016-120000-00000003.json", "20261017-120000-00000002.json"], _files.Deleted.Order());
        Assert.Contains(Id + ".json", _files.Files.Keys);
    }

    [Fact]
    public async Task One_broken_session_does_not_stop_the_others()
    {
        const string newest = "20261019-120000-00000003", broken = "20261019-110000-00000002", oldest = "20261019-100000-00000001";
        _state["a"] = "boost";
        _state["c"] = "boost";
        Put(Session(newest, changes: [Change("a")], startedAt: Start));
        var node = JsonNode.Parse(SnapshotStore.Serialize(Session(broken, startedAt: Start.AddHours(-1))))!;
        node["changes"] = new JsonArray((JsonNode?)null);
        _files.Put(broken + ".json", node.ToJsonString());
        Put(Session(oldest, changes: [Change("c")], startedAt: Start.AddHours(-2)));

        var report = await Recovery().RunAsync(Ct);

        Assert.Equal([newest, oldest], report.Recovered.Select(s => s.SessionId));
        Assert.Equal(["orig", "orig"], new[] { _state["a"], _state["c"] });
        var problem = Assert.Single(report.Problems);
        Assert.Equal(broken + ".json", problem.FileName);
        Assert.Equal(JournalProblemKind.RecoveryFailed, problem.Kind);
    }
}
