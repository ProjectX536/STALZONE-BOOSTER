using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Booster.Core.Profiles;
using Booster.Core.Recovery;
using Booster.Core.Reports;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static Booster.Core.Tests.TestSupport.SessionFactory;

namespace Booster.Core.Tests.Recovery;

public class MaintenanceRunnerTests
{
    private readonly FakeSystemState _state = new();
    private readonly InMemoryJournalFileStore _files = new();
    private readonly FakeTimeProvider _time = new(Start);
    private readonly FakePrivilegeInfo _privileges = new();
    private readonly ChangeJournal _journal;
    private readonly FakeRestorer _restorer;

    public MaintenanceRunnerTests()
    {
        _journal = new ChangeJournal(_files, NullLogger<ChangeJournal>.Instance);
        _restorer = new FakeRestorer(_state, "a");
        _state["a"] = "orig";
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MaintenanceRunner Runner()
    {
        IActionRestorer[] restorers = [_restorer];
        return new MaintenanceRunner(
            new ActionExecutor(new ActionPolicy(), _journal, _time, NullLogger<ActionExecutor>.Instance),
            new RollbackManager(restorers, _journal, _time, NullLogger<RollbackManager>.Instance),
            new RecoveryValidator(restorers, _journal, _time, NullLogger<RecoveryValidator>.Instance),
            _journal,
            _privileges,
            _time,
            NullLogger<MaintenanceRunner>.Instance);
    }

    private FakeAction Action() => new(_state, "a")
    {
        Descriptor = new ActionDescriptor("a", "Действие a", SafetyLevel.L2Confirmed, RequiresAdmin: false, RequiresReboot: false,
            RequiresConfirmation: true, IsReversible: true, AllowedDuringGaming: false, VerifiedForAutomatic: false),
    };

    private async Task<IReadOnlyList<SessionRecord>> Sessions() => (await _journal.LoadAsync(Ct)).Sessions;

    private static ChangeStatus[] Statuses(SessionRecord session) => session.Changes.Select(c => c.Status).ToArray();

    private async Task<SessionRecord> AppliedSession()
    {
        var result = await Runner().ApplyAsync(Action(), userConfirmed: true, gamingSessionActive: false, Ct);
        Assert.Equal(MaintenanceStatus.Applied, result.Status);
        return result.Session!;
    }

    [Theory]
    [InlineData(false, MaintenanceStatus.Refused)]
    [InlineData(true, MaintenanceStatus.Applied)]
    public async Task Admin_action_runs_only_in_elevated_booster(bool elevated, MaintenanceStatus expected)
    {
        _privileges.State = new PrivilegeState(IsElevated: elevated, IsPerformanceLogUser: false);
        var adminAction = new FakeAction(_state, "a") { Descriptor = Action().Descriptor with { RequiresAdmin = true } };

        var result = await Runner().ApplyAsync(adminAction, userConfirmed: true, gamingSessionActive: false, Ct);

        Assert.Equal(expected, result.Status);
        Assert.Equal(elevated ? "boost" : "orig", _state["a"]);
    }

    [Fact]
    public async Task Confirmed_action_stays_applied_in_finished_maintenance_session()
    {
        _time.Advance(TimeSpan.FromMinutes(5));

        var result = await Runner().ApplyAsync(Action(), userConfirmed: true, gamingSessionActive: false, Ct);

        Assert.Equal(MaintenanceStatus.Applied, result.Status);
        Assert.Equal("boost", _state["a"]);
        var stored = Assert.Single(await Sessions());
        Assert.Equal(result.Session!.SessionId, stored.SessionId);
        Assert.Equal(SessionKind.Maintenance, stored.Kind);
        Assert.Equal(MaintenanceRunner.ProfileId, stored.ProfileId);
        Assert.Equal(ExecutionTrigger.UserInitiated, stored.Trigger);
        Assert.Equal(SessionOutcome.Completed, stored.Outcome);
        Assert.Equal(SessionState.Idle, stored.State);
        Assert.Equal(_time.GetUtcNow(), stored.FinishedAt);
        Assert.Equal([ChangeStatus.Applied], Statuses(stored));
        Assert.Equal("a: → boost", stored.Changes[0].Summary);
    }

    [Fact]
    public async Task Unconfirmed_action_is_refused_without_journal()
    {
        var action = Action();

        var result = await Runner().ApplyAsync(action, userConfirmed: false, gamingSessionActive: false, Ct);

        Assert.Equal(MaintenanceStatus.Refused, result.Status);
        Assert.Null(result.Session);
        Assert.Equal(EnumText.Format(PolicyReason.ConfirmationRequired), result.Detail);
        Assert.Equal(0, action.Applies);
        Assert.Equal("orig", _state["a"]);
        Assert.Equal(0, _files.Writes);
    }

    [Fact]
    public async Task Gaming_session_refuses_action_not_allowed_during_gaming()
    {
        var action = Action();

        var result = await Runner().ApplyAsync(action, userConfirmed: true, gamingSessionActive: true, Ct);

        Assert.Equal(MaintenanceStatus.Refused, result.Status);
        Assert.Equal(EnumText.Format(PolicyReason.NotDuringGaming), result.Detail);
        Assert.Equal(0, action.Applies);
        Assert.Empty(await Sessions());
    }

    [Fact]
    public async Task Unsupported_action_creates_no_journal()
    {
        var action = Action();
        action.Support = new ActionSupport(false, "уже отключено", "a");

        var result = await Runner().ApplyAsync(action, userConfirmed: true, gamingSessionActive: false, Ct);

        Assert.Equal(MaintenanceStatus.NotSupported, result.Status);
        Assert.Equal("уже отключено", result.Detail);
        Assert.Null(result.Session);
        Assert.Equal(0, action.Captures);
        Assert.Equal(0, _files.Writes);
    }

    [Fact]
    public async Task Support_check_failure_is_not_supported()
    {
        var action = Action();
        action.ThrowOnCheck = true;

        var result = await Runner().ApplyAsync(action, userConfirmed: true, gamingSessionActive: false, Ct);

        Assert.Equal(MaintenanceStatus.NotSupported, result.Status);
        Assert.Equal("check failed", result.Detail);
        Assert.Equal(0, _files.Writes);
    }

    [Fact]
    public async Task Not_applied_action_is_recorded_as_skipped()
    {
        var action = Action();
        action.ApplyResult = ApplyResult.NotApplied("нет доступа");

        var result = await Runner().ApplyAsync(action, userConfirmed: true, gamingSessionActive: false, Ct);

        Assert.Equal(MaintenanceStatus.NotApplied, result.Status);
        Assert.Equal("нет доступа", result.Detail);
        var stored = Assert.Single(await Sessions());
        Assert.Empty(stored.Changes);
        var skipped = Assert.Single(stored.Skipped);
        Assert.Equal(("a", "Действие a", "нет доступа"), (skipped.ActionId, skipped.DisplayName, skipped.Reason));
        Assert.Equal(SessionOutcome.Completed, stored.Outcome);
        Assert.Equal(SessionState.Idle, stored.State);
    }

    [Fact]
    public async Task Failed_apply_is_rolled_back()
    {
        var action = Action();
        action.OnApply = () => _state["a"] = "boost";
        action.ThrowOnApply = true;

        var result = await Runner().ApplyAsync(action, userConfirmed: true, gamingSessionActive: false, Ct);

        Assert.Equal(MaintenanceStatus.Failed, result.Status);
        Assert.Equal("apply failed", result.Detail);
        Assert.Equal("orig", _state["a"]);
        var stored = Assert.Single(await Sessions());
        Assert.Equal(SessionOutcome.RolledBack, stored.Outcome);
        Assert.Equal(SessionState.Idle, stored.State);
        Assert.Equal([ChangeStatus.Restored], Statuses(stored));
        Assert.True(stored.Changes[0].Verified);
    }

    [Fact]
    public async Task Capture_failure_creates_no_journal()
    {
        var action = Action();
        action.ThrowOnCapture = true;

        var result = await Runner().ApplyAsync(action, userConfirmed: true, gamingSessionActive: false, Ct);

        Assert.Equal(MaintenanceStatus.Failed, result.Status);
        Assert.Null(result.Session);
        Assert.Equal(0, action.Applies);
        Assert.Empty(await Sessions());
    }

    [Fact]
    public async Task Revert_restores_original_and_keeps_session()
    {
        var applied = await AppliedSession();

        var reverted = await Runner().RevertAsync(applied.SessionId, Ct);

        Assert.Equal("orig", _state["a"]);
        Assert.Equal([ChangeStatus.Restored], Statuses(reverted));
        Assert.True(reverted.Changes[0].Verified);
        Assert.Equal(SessionOutcome.Completed, reverted.Outcome);
        Assert.Equal(SessionState.Idle, reverted.State);
        var stored = Assert.Single(await Sessions());
        Assert.Equal([ChangeStatus.Restored], Statuses(stored));
        Assert.Equal(SessionOutcome.Completed, stored.Outcome);
    }

    [Fact]
    public async Task Revert_after_user_change_is_conflict()
    {
        var applied = await AppliedSession();
        _state["a"] = "user";

        var reverted = await Runner().RevertAsync(applied.SessionId, Ct);

        Assert.Equal([ChangeStatus.RestoreConflict], Statuses(reverted));
        Assert.Equal("user", _state["a"]);
    }

    [Fact]
    public async Task Revert_twice_is_idempotent()
    {
        var applied = await AppliedSession();
        var runner = Runner();
        await runner.RevertAsync(applied.SessionId, Ct);
        _state.ClearCalls();

        var again = await runner.RevertAsync(applied.SessionId, Ct);

        Assert.Empty(_state.Calls);
        Assert.Equal([ChangeStatus.Restored], Statuses(again));
        Assert.Equal("orig", _state["a"]);
    }

    [Fact]
    public async Task Revert_of_game_session_throws()
    {
        _state["a"] = "boost";
        await _journal.SaveAsync(Session(changes: [Change("a")], state: SessionState.Idle, outcome: SessionOutcome.Completed), Ct);

        await Assert.ThrowsAsync<ArgumentException>(() => Runner().RevertAsync(Id, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => Runner().RevertAsync("20261019-120000-000000ff", Ct));
        Assert.Equal("boost", _state["a"]);
    }
}
