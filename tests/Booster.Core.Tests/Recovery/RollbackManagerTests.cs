using Booster.Core.Recovery;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static Booster.Core.Tests.TestSupport.SessionFactory;

namespace Booster.Core.Tests.Recovery;

public class RollbackManagerTests
{
    private readonly FakeSystemState _state = new();
    private readonly RecordingJournal _journal = new();
    private readonly FakeTimeProvider _time = new(Start.AddHours(1));
    private readonly Dictionary<string, FakeRestorer> _restorers = new();

    public RollbackManagerTests()
    {
        foreach (var id in new[] { "a", "b", "c" })
            _restorers[id] = new FakeRestorer(_state, id);
    }

    private RollbackManager Manager() => new(_restorers.Values, _journal, _time, NullLogger<RollbackManager>.Instance);

    private SessionRecord ThreeApplied()
    {
        foreach (var id in new[] { "a", "b", "c" })
            _state[id] = "boost";
        return Session(changes: [Change("a"), Change("b"), Change("c")]);
    }

    private static ChangeStatus[] Statuses(SessionRecord session) => session.Changes.Select(c => c.Status).ToArray();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Restore_reverts_in_reverse_order()
    {
        var result = await Manager().RestoreAsync(ThreeApplied(), Ct);

        Assert.Equal(["revert:c", "revert:b", "revert:a"], _state.Calls);
        Assert.All(result.Changes, c => Assert.Equal(ChangeStatus.Restored, c.Status));
        Assert.All(result.Changes, c => Assert.Equal(_time.GetUtcNow(), c.UpdatedAt));
        Assert.Equal(["orig", "orig", "orig"], new[] { _state["a"], _state["b"], _state["c"] });
    }

    [Fact]
    public async Task User_change_after_apply_is_left_alone_and_marked_conflict()
    {
        var session = ThreeApplied();
        _state["b"] = "user";

        var result = await Manager().RestoreAsync(session, Ct);

        Assert.Equal([ChangeStatus.Restored, ChangeStatus.RestoreConflict, ChangeStatus.Restored], Statuses(result));
        Assert.DoesNotContain("revert:b", _state.Calls);
        Assert.Equal("user", _state["b"]);
    }

    [Fact]
    public async Task Restore_is_idempotent()
    {
        var session = ThreeApplied();
        _state["b"] = "user";
        var first = await Manager().RestoreAsync(session, Ct);
        _state.ClearCalls();

        var second = await Manager().RestoreAsync(first, Ct);

        Assert.Empty(_state.Calls);
        Assert.Equal(Statuses(first), Statuses(second));
        Assert.Equal(["orig", "user", "orig"], new[] { _state["a"], _state["b"], _state["c"] });
    }

    [Fact]
    public async Task Pending_with_original_value_is_restored_without_revert()
    {
        _state["a"] = "orig";

        var result = await Manager().RestoreAsync(Session(changes: [Change("a", status: ChangeStatus.Pending)]), Ct);

        Assert.Equal(ChangeStatus.Restored, Assert.Single(result.Changes).Status);
        Assert.Empty(_state.Calls);
    }

    [Fact]
    public async Task Pending_with_applied_value_is_reverted()
    {
        _state["a"] = "boost";

        var result = await Manager().RestoreAsync(Session(changes: [Change("a", status: ChangeStatus.Pending)]), Ct);

        Assert.Equal(ChangeStatus.Restored, Assert.Single(result.Changes).Status);
        Assert.Equal(["revert:a"], _state.Calls);
    }

    [Fact]
    public async Task Gone_object_counts_as_restored()
    {
        var result = await Manager().RestoreAsync(Session(changes: [Change("a")]), Ct);

        Assert.Equal(ChangeStatus.Restored, Assert.Single(result.Changes).Status);
        Assert.Empty(_state.Calls);
    }

    [Fact]
    public async Task Failure_of_one_restore_does_not_stop_others()
    {
        _restorers["b"].ThrowOnRevert = true;

        var result = await Manager().RestoreAsync(ThreeApplied(), Ct);

        Assert.Equal([ChangeStatus.Restored, ChangeStatus.RestoreFailed, ChangeStatus.Restored], Statuses(result));
        Assert.Equal("revert failed", result.Changes[1].Detail);
        Assert.Equal("orig", _state["a"]);
    }

    [Fact]
    public async Task RestoreFailed_is_retried_on_next_pass()
    {
        _restorers["b"].ThrowOnRevert = true;
        var first = await Manager().RestoreAsync(ThreeApplied(), Ct);
        _restorers["b"].ThrowOnRevert = false;
        _state.ClearCalls();

        var second = await Manager().RestoreAsync(first, Ct);

        Assert.Equal(["revert:b"], _state.Calls);
        Assert.All(second.Changes, c => Assert.Equal(ChangeStatus.Restored, c.Status));
        Assert.Null(second.Changes[1].Detail);
    }

    [Fact]
    public async Task Journal_save_failure_does_not_stop_restore()
    {
        _journal.FailWith = _ => new IOException("disk full");

        var result = await Manager().RestoreAsync(ThreeApplied(), Ct);

        Assert.All(result.Changes, c => Assert.Equal(ChangeStatus.Restored, c.Status));
        Assert.Equal(["orig", "orig", "orig"], new[] { _state["a"], _state["b"], _state["c"] });
    }

    [Fact]
    public async Task Revert_without_effect_is_RestoreFailed()
    {
        _restorers["a"].RevertHasNoEffect = true;
        _state["a"] = "boost";

        var result = await Manager().RestoreAsync(Session(changes: [Change("a")]), Ct);

        var entry = Assert.Single(result.Changes);
        Assert.Equal(ChangeStatus.RestoreFailed, entry.Status);
        Assert.False(string.IsNullOrWhiteSpace(entry.Detail));
    }

    [Fact]
    public async Task Irreversible_change_needs_manual_check()
    {
        _state["a"] = "boost";
        var change = Change("a", reversible: false) with { Detail = "Закрыто: chrome" };

        var result = await Manager().RestoreAsync(Session(changes: [change]), Ct);

        var entry = Assert.Single(result.Changes);
        Assert.Equal(ChangeStatus.NeedsManualCheck, entry.Status);
        Assert.StartsWith("Закрыто: chrome; ", entry.Detail);
        Assert.Empty(_state.Calls);
        Assert.Equal(0, _restorers["a"].Inspections);
    }

    [Fact]
    public async Task Unknown_action_or_newer_snapshot_needs_manual_check()
    {
        _state["a"] = "boost";
        _state["x"] = "boost";
        var newer = Change("a") with { Snapshot = Change("a").Snapshot with { SchemaVersion = 2 } };

        var result = await Manager().RestoreAsync(Session(changes: [Change("x"), newer]), Ct);

        Assert.Equal([ChangeStatus.NeedsManualCheck, ChangeStatus.NeedsManualCheck], Statuses(result));
        Assert.All(result.Changes, c => Assert.False(string.IsNullOrWhiteSpace(c.Detail)));
        Assert.Empty(_state.Calls);
    }

    [Fact]
    public async Task Reboot_change_becomes_NeedsReboot()
    {
        _state["a"] = "boost";

        var result = await Manager().RestoreAsync(Session(changes: [Change("a", requiresReboot: true)]), Ct);

        Assert.Equal(ChangeStatus.NeedsReboot, Assert.Single(result.Changes).Status);
    }

    [Fact]
    public async Task Journal_is_saved_after_each_change()
    {
        await Manager().RestoreAsync(ThreeApplied(), Ct);

        Assert.Equal(3, _journal.Saves.Count);
        Assert.Equal([ChangeStatus.Applied, ChangeStatus.Applied, ChangeStatus.Restored], Statuses(_journal.Saves[0]));
        Assert.Equal([ChangeStatus.Applied, ChangeStatus.Restored, ChangeStatus.Restored], Statuses(_journal.Saves[1]));
        Assert.Equal([ChangeStatus.Restored, ChangeStatus.Restored, ChangeStatus.Restored], Statuses(_journal.Saves[2]));
    }

    [Fact]
    public async Task Final_statuses_are_not_touched()
    {
        _state["a"] = "boost";
        _state["b"] = "boost";
        var session = Session(changes:
        [
            Change("a", status: ChangeStatus.RestoreConflict),
            Change("b", status: ChangeStatus.NeedsManualCheck),
        ]);

        var result = await Manager().RestoreAsync(session, Ct);

        Assert.Equal([ChangeStatus.RestoreConflict, ChangeStatus.NeedsManualCheck], Statuses(result));
        Assert.Empty(_state.Calls);
        Assert.Empty(_journal.Saves);
    }

    [Fact]
    public async Task Cancellation_keeps_saved_statuses()
    {
        using var cts = new CancellationTokenSource();
        _restorers["b"].OnRevert = _ => cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Manager().RestoreAsync(ThreeApplied(), cts.Token));

        Assert.Equal([ChangeStatus.Applied, ChangeStatus.Applied, ChangeStatus.Restored], Statuses(_journal.Last!));
        Assert.Equal("boost", _state["a"]);
    }
}
