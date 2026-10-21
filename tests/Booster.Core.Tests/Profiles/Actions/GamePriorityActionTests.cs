using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Profiles;
using Booster.Core.Profiles.Actions;
using Booster.Core.Recovery;
using Booster.Core.Reports;
using Booster.Core.Resources;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static Booster.Core.Tests.TestSupport.SessionFactory;

namespace Booster.Core.Tests.Profiles.Actions;

public class GamePriorityActionTests
{
    private const int Pid = 1234;

    private readonly FakeProcessProvider _processes = new();
    private readonly FakeTimeProvider _time = new(Start);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GameProcess StartGame(ProcessPriority priority = ProcessPriority.Normal, DateTimeOffset? startTime = null) =>
        new("cs2", _processes.Start("cs2", Pid, startTime, priority));

    private GamePriorityAction Action(GameProcess? game, ProcessPriority target = ProcessPriority.AboveNormal) =>
        new(_processes, game, target, _time);

    private RollbackManager Rollback() =>
        new([new GamePriorityRestorer(_processes, _processes)], new RecordingJournal(), _time, NullLogger<RollbackManager>.Instance);

    private static ChangeEntry Entry(ActionSnapshot snapshot) => Change(ActionIds.GamePriority) with { Snapshot = snapshot };

    private static async Task<ActionSnapshot> ApplyAsync(GamePriorityAction action)
    {
        Assert.True((await action.CheckSupportAsync(Ct)).IsSupported);
        var snapshot = await action.CaptureAsync(Ct);
        await action.ApplyAsync(Ct);
        return snapshot;
    }

    [Fact]
    public async Task Restore_ignores_process_with_reused_pid()
    {
        var snapshot = await ApplyAsync(Action(StartGame()));
        _processes.Exit(Pid);
        _processes.Start("notepad", Pid, Start.AddMinutes(30));
        var calls = _processes.PriorityCalls.Count;

        Assert.Equal(ObservedState.Gone, await new GamePriorityRestorer(_processes, _processes).InspectAsync(snapshot, Ct));
        var restored = await Rollback().RestoreAsync(Session(changes: [Entry(snapshot)]), Ct);

        Assert.Equal(ChangeStatus.Restored, restored.Changes[0].Status);
        Assert.Equal(calls, _processes.PriorityCalls.Count);
        Assert.Equal(ProcessPriority.Normal, _processes.GetPriority(new ProcessIdentity(Pid, Start.AddMinutes(30))));
    }

    [Fact]
    public async Task Exited_game_needs_no_restore()
    {
        var snapshot = await ApplyAsync(Action(StartGame()));
        _processes.Exit(Pid);

        var restored = await Rollback().RestoreAsync(Session(changes: [Entry(snapshot)]), Ct);

        Assert.Equal(ChangeStatus.Restored, restored.Changes[0].Status);
        Assert.Single(_processes.PriorityCalls);
    }

    [Fact]
    public async Task Running_game_with_unreadable_priority_is_not_reported_as_restored()
    {
        var snapshot = await ApplyAsync(Action(StartGame()));
        _processes.DenyRead(Pid);

        var restored = await Rollback().RestoreAsync(Session(changes: [Entry(snapshot)]), Ct);

        Assert.Equal(ChangeStatus.RestoreFailed, restored.Changes[0].Status);
        Assert.NotNull(restored.Changes[0].Detail);
        Assert.Single(_processes.PriorityCalls);
    }

    [Fact]
    public async Task Running_game_gets_its_original_priority_back()
    {
        var game = StartGame();
        var snapshot = await ApplyAsync(Action(game));

        var restored = await Rollback().RestoreAsync(Session(changes: [Entry(snapshot)]), Ct);

        Assert.Equal(ChangeStatus.Restored, restored.Changes[0].Status);
        Assert.Equal([(game.Identity, ProcessPriority.AboveNormal), (game.Identity, ProcessPriority.Normal)], _processes.PriorityCalls);
    }

    [Fact]
    public async Task Priority_raised_by_the_game_itself_is_a_conflict()
    {
        var snapshot = await ApplyAsync(Action(StartGame()));
        _processes.ChangePriority(Pid, ProcessPriority.High);

        var restored = await Rollback().RestoreAsync(Session(changes: [Entry(snapshot)]), Ct);

        Assert.Equal(ChangeStatus.RestoreConflict, restored.Changes[0].Status);
        Assert.Single(_processes.PriorityCalls);
        Assert.Equal(ProcessPriority.High, _processes.GetPriority(new ProcessIdentity(Pid, Start)));
    }

    [Fact]
    public async Task Start_time_with_sub_second_ticks_survives_the_snapshot()
    {
        var startTime = new DateTimeOffset(2026, 10, 19, 15, 0, 0, TimeSpan.FromHours(3)).AddTicks(1_234_567);
        var snapshot = await ApplyAsync(Action(StartGame(startTime: startTime)));

        Assert.Equal(ObservedState.Applied, await new GamePriorityRestorer(_processes, _processes).InspectAsync(snapshot, Ct));
    }

    [Theory]
    [InlineData(ProcessPriority.AboveNormal, ProcessPriority.AboveNormal)]
    [InlineData(ProcessPriority.High, ProcessPriority.AboveNormal)]
    [InlineData(ProcessPriority.RealTime, ProcessPriority.High)]
    public async Task Priority_already_at_or_above_target_is_not_lowered(ProcessPriority current, ProcessPriority target)
    {
        var support = await Action(StartGame(current), target).CheckSupportAsync(Ct);

        Assert.False(support.IsSupported);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, ReportStrings.Skip_PriorityAlreadyFormat, EnumText.Format(current)), support.Reason);
        Assert.Empty(_processes.PriorityCalls);
    }

    [Fact]
    public async Task Higher_priority_reason_names_it()
    {
        var support = await Action(StartGame(ProcessPriority.High)).CheckSupportAsync(Ct);

        Assert.Equal("приоритет уже высокий", support.Reason);
    }

    [Fact]
    public async Task No_access_to_the_game_is_not_supported()
    {
        var game = StartGame();
        _processes.DenyAccess(Pid);

        var support = await Action(game).CheckSupportAsync(Ct);

        Assert.False(support.IsSupported);
        Assert.Equal(ReportStrings.Skip_NoAccessToGame, support.Reason);
    }

    [Fact]
    public async Task Access_denied_is_reported_as_not_applied()
    {
        var action = Action(StartGame());
        Assert.True((await action.CheckSupportAsync(Ct)).IsSupported);
        await action.CaptureAsync(Ct);
        _processes.DenyAccess(Pid);

        var result = await action.ApplyAsync(Ct);

        Assert.False(result.Applied);
        Assert.Equal("нет доступа к процессу игры (возможно, защищён античитом или запущен от администратора)", result.Detail);
        Assert.Equal(ReportStrings.Skip_NoAccessToGame, result.Detail);
        Assert.Empty(_processes.PriorityCalls);
    }

    [Fact]
    public async Task Game_that_is_gone_is_not_supported()
    {
        var game = StartGame();
        _processes.Exit(Pid);

        var support = await Action(game).CheckSupportAsync(Ct);

        Assert.False(support.IsSupported);
        Assert.Equal(ReportStrings.Skip_GameUnavailable, support.Reason);
    }

    [Fact]
    public async Task Without_a_game_it_is_checked_at_start_and_cannot_be_captured()
    {
        var action = Action(null);

        var support = await action.CheckSupportAsync(Ct);

        Assert.True(support.IsSupported);
        Assert.Equal("Приоритет игры → выше среднего (проверяется при запуске)", support.Summary);
        await Assert.ThrowsAsync<InvalidOperationException>(() => action.CaptureAsync(Ct));
    }

    [Fact]
    public async Task Summary_names_the_game_and_both_priorities()
    {
        var support = await Action(StartGame()).CheckSupportAsync(Ct);

        Assert.Equal("cs2: обычный → выше среднего", support.Summary);
    }

    [Fact]
    public async Task Capture_records_process_identity_name_and_priorities()
    {
        var snapshot = await Action(StartGame()).CaptureAsync(Ct);

        Assert.Equal(1, snapshot.SchemaVersion);
        Assert.Equal(ActionIds.GamePriority, snapshot.ActionId);
        Assert.Equal(Start, snapshot.CapturedAt);
        Assert.Equal(Pid, snapshot.OriginalValue.GetProperty("processId").GetInt32());
        Assert.Equal(Start, snapshot.OriginalValue.GetProperty("startTime").GetDateTimeOffset());
        Assert.Equal("cs2", snapshot.OriginalValue.GetProperty("name").GetString());
        Assert.Equal("Normal", snapshot.OriginalValue.GetProperty("priority").GetString());
        Assert.Equal("AboveNormal", snapshot.AppliedValue!.Value.GetProperty("priority").GetString());
    }

    [Fact]
    public async Task Only_apply_raises_the_priority_and_exactly_once()
    {
        var game = StartGame();
        var action = Action(game);

        await action.CheckSupportAsync(Ct);
        await action.CaptureAsync(Ct);
        Assert.Empty(_processes.PriorityCalls);

        await action.ApplyAsync(Ct);

        Assert.Equal([(game.Identity, ProcessPriority.AboveNormal)], _processes.PriorityCalls);
    }

    [Fact]
    public async Task Apply_without_capture_changes_nothing()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Action(StartGame()).ApplyAsync(Ct));

        Assert.Empty(_processes.PriorityCalls);
    }

    [Fact]
    public void Optional_not_enabled_is_not_run()
    {
        var decision = new ActionPolicy().Evaluate(Action(null).Descriptor, ActionMode.Optional,
            new PolicyContext(ExecutionTrigger.Automatic, IsGamingSessionActive: false, UserEnabled: false, UserConfirmed: false, CanElevate: false));

        Assert.Equal(PolicyVerdict.NotEnabled, decision.Verdict);
    }

    [Fact]
    public void Optional_enabled_runs_in_autostart()
    {
        var decision = new ActionPolicy().Evaluate(Action(null).Descriptor, ActionMode.Optional,
            new PolicyContext(ExecutionTrigger.Automatic, IsGamingSessionActive: false, UserEnabled: true, UserConfirmed: false, CanElevate: false));

        Assert.Equal(PolicyVerdict.Allow, decision.Verdict);
    }

    [Fact]
    public void Descriptor_is_verified_reversible_level_one()
    {
        Assert.Equal(new ActionDescriptor(ActionIds.GamePriority, ReportStrings.Action_GamePriority, SafetyLevel.L1Reversible,
            RequiresAdmin: false, RequiresReboot: false, RequiresConfirmation: false, IsReversible: true,
            AllowedDuringGaming: true, VerifiedForAutomatic: true), Action(null).Descriptor);
    }
}
