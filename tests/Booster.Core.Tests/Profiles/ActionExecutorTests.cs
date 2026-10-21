using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Profiles;
using Booster.Core.Recovery;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static Booster.Core.Tests.TestSupport.SessionFactory;

namespace Booster.Core.Tests.Profiles;

public class ActionExecutorTests
{
    private static readonly PolicyContext Context = new(ExecutionTrigger.UserInitiated, IsGamingSessionActive: true,
        UserEnabled: false, UserConfirmed: true, CanElevate: false);

    private readonly FakeSystemState _state = new();
    private readonly RecordingJournal _journal = new();
    private readonly FakeTimeProvider _time = new(Start.AddMinutes(5));
    private readonly StubPolicy _policy = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ActionExecutor Executor() => new(_policy, _journal, _time, NullLogger<ActionExecutor>.Instance);

    private FakeAction Action(string id = "a")
    {
        _state[id] = "orig";
        return new FakeAction(_state, id);
    }

    private Task<ExecutionResult> Execute(FakeAction action, CancellationToken? ct = null) =>
        Executor().ExecuteAsync(Session(), action, "orig → boost", ActionMode.Allowed, Context, ct ?? Ct);

    [Theory]
    [InlineData(PolicyVerdict.RequireConfirmation)]
    [InlineData(PolicyVerdict.RecommendOnly)]
    [InlineData(PolicyVerdict.NotEnabled)]
    [InlineData(PolicyVerdict.Deny)]
    public async Task Action_not_allowed_by_policy_is_refused_untouched(PolicyVerdict verdict)
    {
        _policy.Decision = new PolicyDecision(verdict, PolicyReason.ConfirmationRequired);
        var action = Action();

        var result = await Execute(action);

        Assert.Equal(ExecutionStatus.Refused, result.Status);
        Assert.Equal(_policy.Decision, result.Decision);
        Assert.Equal(0, action.Captures);
        Assert.Equal(0, action.Applies);
        Assert.Empty(_journal.Saves);
        Assert.Empty(result.Session.Changes);
        Assert.Equal("orig", _state["a"]);
    }

    [Fact]
    public async Task Executor_evaluates_policy_itself()
    {
        var action = Action();

        await Executor().ExecuteAsync(Session(), action, "s", ActionMode.AskFirst, Context, Ct);

        var (descriptor, mode, context) = Assert.Single(_policy.Calls);
        Assert.Same(action.Descriptor, descriptor);
        Assert.Equal(ActionMode.AskFirst, mode);
        Assert.Same(Context, context);
    }

    [Fact]
    public async Task Old_value_is_saved_before_apply()
    {
        var action = Action();
        SessionRecord? atApply = null;
        action.OnApply = () => atApply = _journal.Last;

        await Execute(action);

        var entry = Assert.Single(atApply!.Changes);
        Assert.Equal(ChangeStatus.Pending, entry.Status);
        Assert.Equal("orig", entry.Snapshot.OriginalValue.GetString());
        Assert.Equal("boost", entry.Snapshot.AppliedValue?.GetString());
    }

    [Fact]
    public async Task Successful_apply_saves_pending_then_applied()
    {
        var action = Action();
        action.ApplyResult = new ApplyResult(Json("data"), "detail");

        var result = await Execute(action);

        Assert.Equal(ExecutionStatus.Applied, result.Status);
        Assert.Null(result.Error);
        Assert.Equal([ChangeStatus.Pending, ChangeStatus.Applied], _journal.Saves.Select(s => Assert.Single(s.Changes).Status));
        Assert.Same(result.Session, _journal.Last);
        var entry = Assert.Single(result.Session.Changes);
        Assert.Equal("a", entry.ActionId);
        Assert.Equal("Действие a", entry.DisplayName);
        Assert.Equal("orig → boost", entry.Summary);
        Assert.Equal("data", entry.Result?.GetString());
        Assert.Equal("detail", entry.Detail);
        Assert.Null(entry.Verified);
        Assert.Equal(_time.GetUtcNow(), entry.UpdatedAt);
        Assert.Equal("boost", _state["a"]);
    }

    [Fact]
    public async Task Entry_flags_come_from_descriptor()
    {
        _state["a"] = "orig";
        var action = new FakeAction(_state, "a")
        {
            Descriptor = new ActionDescriptor("a", "Закрыть", SafetyLevel.L2Confirmed, RequiresAdmin: false, RequiresReboot: true,
                RequiresConfirmation: true, IsReversible: false, AllowedDuringGaming: true, VerifiedForAutomatic: false),
        };

        var result = await Execute(action);

        var entry = Assert.Single(result.Session.Changes);
        Assert.Equal("Закрыть", entry.DisplayName);
        Assert.False(entry.IsReversible);
        Assert.True(entry.RequiresReboot);
    }

    [Fact]
    public async Task Apply_failure_leaves_pending_entry_with_detail()
    {
        var action = Action();
        action.ThrowOnApply = true;

        var result = await Execute(action);

        Assert.Equal(ExecutionStatus.Failed, result.Status);
        Assert.IsType<InvalidOperationException>(result.Error);
        var entry = Assert.Single(result.Session.Changes);
        Assert.Equal(ChangeStatus.Pending, entry.Status);
        Assert.Equal("apply failed", entry.Detail);
        Assert.Equal(2, _journal.Saves.Count);
        Assert.Same(result.Session, _journal.Last);
    }

    [Fact]
    public async Task Not_applied_removes_the_pending_change()
    {
        var action = Action();
        action.ApplyResult = ApplyResult.NotApplied("нет доступа");

        var result = await Execute(action);

        Assert.Equal(ExecutionStatus.NotApplied, result.Status);
        Assert.Null(result.Error);
        Assert.Equal("нет доступа", result.Detail);
        Assert.Empty(result.Session.Changes);
        Assert.Equal(2, _journal.Saves.Count);
        Assert.Equal(ChangeStatus.Pending, Assert.Single(_journal.Saves[0].Changes).Status);
        Assert.Same(result.Session, _journal.Last);
        Assert.Equal("orig", _state["a"]);
    }

    [Fact]
    public async Task Capture_failure_writes_nothing()
    {
        var action = Action();
        action.ThrowOnCapture = true;

        var result = await Execute(action);

        Assert.Equal(ExecutionStatus.Failed, result.Status);
        Assert.NotNull(result.Error);
        Assert.Equal(0, action.Applies);
        Assert.Empty(_journal.Saves);
        Assert.Empty(result.Session.Changes);
    }

    [Fact]
    public async Task Snapshot_of_other_action_is_not_applied()
    {
        var action = Action();
        action.SnapshotActionId = "other";

        var result = await Execute(action);

        Assert.Equal(ExecutionStatus.Failed, result.Status);
        Assert.NotNull(result.Error);
        Assert.Equal(0, action.Applies);
        Assert.Empty(_journal.Saves);
    }

    [Fact]
    public async Task Journal_failure_before_apply_means_no_apply()
    {
        var action = Action();
        _journal.FailWith = _ => new IOException("disk full");

        var result = await Execute(action);

        Assert.Equal(ExecutionStatus.Failed, result.Status);
        Assert.IsType<IOException>(result.Error);
        Assert.Equal(0, action.Applies);
        Assert.Empty(result.Session.Changes);
    }

    [Fact]
    public async Task Journal_failure_after_apply_keeps_applied_result()
    {
        var action = Action();
        _journal.FailWith = s => s.Changes[0].Status == ChangeStatus.Applied ? new IOException("disk full") : null;

        var result = await Execute(action);

        Assert.Equal(ExecutionStatus.Applied, result.Status);
        Assert.Equal(ChangeStatus.Applied, Assert.Single(result.Session.Changes).Status);
        Assert.Equal(ChangeStatus.Pending, Assert.Single(Assert.Single(_journal.Saves).Changes).Status);
    }

    [Fact]
    public async Task Cancellation_after_pending_keeps_the_record()
    {
        var action = Action();
        using var cts = new CancellationTokenSource();
        _journal.FailWith = _ =>
        {
            cts.Cancel();
            return null;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Execute(action, cts.Token));

        Assert.Equal(ChangeStatus.Pending, Assert.Single(Assert.Single(_journal.Saves).Changes).Status);
        Assert.Equal("orig", _state["a"]);
    }

    private sealed class StubPolicy : IActionPolicy
    {
        public PolicyDecision Decision { get; set; } = new(PolicyVerdict.Allow, PolicyReason.Allowed);

        public List<(ActionDescriptor Action, ActionMode? Mode, PolicyContext Context)> Calls { get; } = [];

        public PolicyDecision Evaluate(ActionDescriptor action, ActionMode? configuredMode, PolicyContext context)
        {
            Calls.Add((action, configuredMode, context));
            return Decision;
        }
    }
}
