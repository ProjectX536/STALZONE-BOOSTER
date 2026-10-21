using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Profiles;
using Booster.Core.Recovery;
using Booster.Core.Resources;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static Booster.Core.Tests.TestSupport.SessionFactory;

namespace Booster.Core.Tests.Profiles;

public sealed class GameSessionCoordinatorTests : IAsyncDisposable
{
    private const int GamePid = 4242;
    private const string ProfileId = "competitive";

    /// <summary>FakeConfiguration: gameDetectionIntervalMs = 2000.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly FakeSystemState _state = new();
    private readonly RecordingJournal _journal = new();
    private readonly FakeTimeProvider _time = new(Start);
    private readonly FakeActionCatalog _catalog = new();
    private readonly FakeProcessProvider _processes = new();
    private readonly FakeProfileRepository _profiles = new();
    private readonly FakeConfiguration _config = new();
    private readonly ListLogger<GameSessionCoordinator> _log = new();
    private readonly List<IActionRestorer> _restorers = [];
    private readonly Dictionary<string, FakeAction> _actions = new(StringComparer.Ordinal);
    private GameSessionCoordinator? _coordinator;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Создаётся при первом обращении, когда действия и восстановители уже добавлены.</summary>
    private GameSessionCoordinator Coordinator => _coordinator ??= CreateCoordinator();

    private CoordinatorStatus Status => Coordinator.Status;

    public async ValueTask DisposeAsync()
    {
        if (_coordinator is not null)
            await _coordinator.DisposeAsync();
    }

    private GameSessionCoordinator CreateCoordinator()
    {
        var policy = new ActionPolicy();
        var manager = new GamingProfileManager(_catalog, policy,
            new ActionExecutor(policy, _journal, _time, NullLogger<ActionExecutor>.Instance),
            new RollbackManager(_restorers, _journal, _time, NullLogger<RollbackManager>.Instance),
            new RecoveryValidator(_restorers, _journal, _time, NullLogger<RecoveryValidator>.Instance),
            _journal, _processes, new FakeSystemLoad(), new FakeSensorProvider(_time),
            new SessionFrameCapture(new FakeFrameCaptureProvider(_time), _config, NullLogger<SessionFrameCapture>.Instance), _config, _time,
            NullLogger<GamingProfileManager>.Instance);
        return new GameSessionCoordinator(manager, _processes, _profiles, _config, _time, _log);
    }

    /// <summary>Профиль с игрой cs2 и действиями id → режим; у каждого действия свой фейк и восстановитель.</summary>
    private void UseProfile(bool autoStart, params (string Id, ActionMode Mode)[] actions)
    {
        foreach (var (id, _) in actions)
        {
            if (_actions.ContainsKey(id))
                continue;
            _state[id] = "orig";
            var action = new FakeAction(_state, id);
            _actions[id] = action;
            _catalog.Add(action);
            _restorers.Add(new FakeRestorer(_state, id));
        }

        _profiles.Current = new ProfileSet(
            [ProfileFactory.Profile(games: ["cs2"], autoStart: autoStart,
                actions: actions.Select(a => new ProfileActionEntry(a.Id, a.Mode)).ToArray())],
            []);
    }

    /// <summary>Действие <paramref name="id"/> профиля становится необратимым, как закрытие фоновых приложений.</summary>
    private FakeAction Irreversible(string id)
    {
        var action = new FakeAction(_state, id)
        {
            Descriptor = new ActionDescriptor(id, $"Действие {id}", SafetyLevel.L2Confirmed, RequiresAdmin: false, RequiresReboot: false,
                RequiresConfirmation: true, IsReversible: false, AllowedDuringGaming: false, VerifiedForAutomatic: false),
        };
        _actions[id] = action;
        _catalog.Add(action);
        return action;
    }

    private ProcessIdentity StartGame(int pid = GamePid, DateTimeOffset? startTime = null) => _processes.Start("cs2", pid, startTime);

    private int Applies => _actions.Values.Sum(a => a.Applies);

    private static IReadOnlySet<string> Confirmed(params string[] ids) => ids.ToHashSet(StringComparer.Ordinal);

    private static async Task WaitFor(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(Wait);
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    /// <summary>Двигает время по интервалу опроса, пока условие не выполнится.</summary>
    private async Task AdvanceUntil(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(Wait);
        while (!condition())
        {
            _time.Advance(Interval);
            await Task.Delay(10, timeout.Token);
        }
    }

    /// <summary>Ещё <paramref name="count"/> опросов списка процессов.</summary>
    private Task Polls(int count)
    {
        var target = _processes.ListCalls + count;
        return AdvanceUntil(() => _processes.ListCalls >= target);
    }

    /// <summary>Время идёт <paramref name="intervals"/> интервалов; для проверок, что опроса нет.</summary>
    private async Task Idle(int intervals)
    {
        for (var i = 0; i < intervals; i++)
        {
            _time.Advance(Interval);
            await Task.Delay(20, Ct);
        }
    }

    private Task WaitForGaming() => WaitFor(() => Status is { Mode: CoordinatorMode.InSession, Progress.State: SessionState.Gaming });

    private async Task<SessionRecord> StopAndWait()
    {
        var previous = Status.LastSession;
        Coordinator.StopSession();
        await WaitFor(() => Status.Mode != CoordinatorMode.InSession && Status.LastSession is not null && Status.LastSession != previous);
        return Status.LastSession!;
    }

    [Fact]
    public async Task Does_not_poll_without_arm_or_autostart()
    {
        UseProfile(autoStart: false, ("b", ActionMode.Allowed));
        StartGame();
        await Coordinator.StartAsync(Ct);

        await Idle(5);

        Assert.Equal(0, _processes.ListCalls);
        Assert.Equal(CoordinatorMode.Off, Status.Mode);
        Assert.Equal(0, Applies);

        // Контроль: цикл жив и опрашивает, как только появляется профиль с автозапуском.
        UseProfile(autoStart: true, ("b", ActionMode.Allowed));
        await AdvanceUntil(() => _processes.ListCalls > 0);
    }

    [Fact]
    public async Task Armed_session_starts_when_game_appears()
    {
        UseProfile(autoStart: false, ("a", ActionMode.AskFirst));
        await Coordinator.StartAsync(Ct);
        await Coordinator.ArmAsync(ProfileId, Confirmed("a"), Ct);
        Assert.Equal(CoordinatorMode.Armed, Status.Mode);
        Assert.Equal(ProfileId, Status.ArmedProfileId);

        await Polls(2);
        Assert.Equal(0, Applies);

        StartGame();
        await AdvanceUntil(() => Status.Progress?.State == SessionState.Gaming);

        Assert.Equal(ExecutionTrigger.UserInitiated, Status.Plan?.Trigger);
        Assert.Null(Status.ArmedProfileId);
        Assert.Equal("boost", _state["a"]);

        var session = await StopAndWait();
        Assert.Equal(ExecutionTrigger.UserInitiated, session.Trigger);
        Assert.Equal(SessionOutcome.Completed, session.Outcome);
        Assert.Equal("orig", _state["a"]);
    }

    [Fact]
    public async Task Armed_session_starts_at_once_if_game_is_running()
    {
        UseProfile(autoStart: false, ("a", ActionMode.AskFirst));
        StartGame();
        await Coordinator.StartAsync(Ct);

        var result = await Coordinator.ArmAsync(ProfileId, Confirmed("a"), Ct);

        // Время не двигается: подготовка будит опрос сразу.
        await WaitForGaming();
        Assert.Equal("boost", _state["a"]);

        // Необратимых действий не подтверждали — и пропускать нечего.
        Assert.Equal(new PreparationResult(null, null), result);
    }

    [Fact]
    public async Task Arming_while_the_game_runs_skips_closing()
    {
        UseProfile(autoStart: false, ("a", ActionMode.AskFirst), ("close", ActionMode.AskFirst));
        var close = Irreversible("close");
        StartGame();
        await Coordinator.StartAsync(Ct);

        var result = await Coordinator.ArmAsync(ProfileId, Confirmed("a", "close"), Ct);

        Assert.Null(result.Preparation);
        Assert.Equal(ReportStrings.Skip_OnlyBeforeGame, result.SkippedReason);
        await WaitForGaming();
        Assert.Equal("boost", _state["a"]);
        Assert.Equal(0, close.Applies);
    }

    [Fact]
    public async Task Arming_prepares_then_waits_for_the_game()
    {
        UseProfile(autoStart: false, ("a", ActionMode.AskFirst), ("close", ActionMode.AskFirst));
        var close = Irreversible("close");
        await Coordinator.StartAsync(Ct);

        var result = await Coordinator.ArmAsync(ProfileId, Confirmed("a", "close"), Ct);

        Assert.Null(result.SkippedReason);
        Assert.Equal(SessionKind.Preparation, result.Preparation?.Kind);
        Assert.Equal(ChangeStatus.NeedsManualCheck, Assert.Single(result.Preparation!.Changes).Status);
        Assert.Equal(1, close.Applies);
        Assert.Equal("orig", _state["a"]);
        Assert.Equal(CoordinatorMode.Armed, Status.Mode);
        Assert.Equal(ProfileId, Status.ArmedProfileId);

        StartGame();
        await AdvanceUntil(() => Status.Progress?.State == SessionState.Gaming);

        Assert.Equal("boost", _state["a"]);
        Assert.Equal(1, close.Applies);
        Assert.False(Status.Plan!.Actions.Single(p => p.Action.Id == "close").WillApply);
    }

    [Fact]
    public async Task Arm_is_consumed_by_one_session()
    {
        UseProfile(autoStart: false, ("a", ActionMode.AskFirst));
        StartGame();
        await Coordinator.StartAsync(Ct);
        await Coordinator.ArmAsync(ProfileId, Confirmed("a"), Ct);
        await WaitForGaming();
        Assert.Null(Status.ArmedProfileId);
        await StopAndWait();

        _processes.Exit(GamePid);
        StartGame(GamePid + 1, Start.AddMinutes(5));
        await Idle(5);

        Assert.Equal(1, Applies);
        Assert.Equal(CoordinatorMode.Off, Status.Mode);
    }

    [Fact]
    public async Task Armed_session_runs_the_profile_as_confirmed()
    {
        UseProfile(autoStart: false, ("a", ActionMode.AskFirst), ("b", ActionMode.Optional));
        await Coordinator.StartAsync(Ct);
        await Coordinator.ArmAsync(ProfileId, Confirmed("a"), Ct);

        // Профиль сохранён после подтверждения: изменения действуют со следующей подготовки.
        _profiles.Current = new ProfileSet(
            [ProfileFactory.Profile(games: ["cs2"], enabledOptional: ["b"],
                actions: [new("a", ActionMode.AskFirst), new("b", ActionMode.Optional)])],
            []);
        StartGame();
        await AdvanceUntil(() => Status.Progress?.State == SessionState.Gaming);

        Assert.Equal("boost", _state["a"]);
        Assert.Equal("orig", _state["b"]);
        Assert.Equal(["a", "b"], Status.Plan!.Actions.Select(a => a.Action.Id));
        Assert.False(Status.Plan.Actions[1].WillApply);
    }

    [Fact]
    public async Task Autostart_applies_only_actions_without_confirmation()
    {
        UseProfile(autoStart: true, ("a", ActionMode.AskFirst), ("b", ActionMode.Allowed));
        StartGame();
        await Coordinator.StartAsync(Ct);

        await WaitForGaming();

        Assert.Equal(ExecutionTrigger.Automatic, Status.Plan?.Trigger);
        Assert.Equal(PolicyVerdict.RequireConfirmation, Status.Plan!.Actions.Single(a => a.Action.Id == "a").Decision.Verdict);
        Assert.Equal("orig", _state["a"]);
        Assert.Equal("boost", _state["b"]);

        var session = await StopAndWait();
        Assert.Equal(["a"], session.Skipped.Select(s => s.ActionId));
        Assert.Equal(["b"], session.Changes.Select(c => c.ActionId));
    }

    [Fact]
    public async Task Stopped_session_does_not_restart_for_the_same_game_process()
    {
        UseProfile(autoStart: true, ("b", ActionMode.Allowed));
        StartGame();
        await Coordinator.StartAsync(Ct);
        await WaitForGaming();

        await StopAndWait();
        await Polls(5);

        Assert.Equal(1, Applies);
        Assert.Equal(CoordinatorMode.Watching, Status.Mode);
        Assert.Equal("orig", _state["b"]);

        // Тот же PID, но другое время старта — это новый запуск игры.
        _processes.Exit(GamePid);
        StartGame(GamePid, Start.AddMinutes(5));
        await AdvanceUntil(() => Status.Progress?.State == SessionState.Gaming);

        Assert.Equal(2, Applies);
        Assert.Equal(Start.AddMinutes(5), Status.Plan?.Game?.Identity.StartTime);
    }

    [Fact]
    public async Task Second_game_gets_a_session_while_an_ignored_game_keeps_running()
    {
        UseProfile(autoStart: true, ("b", ActionMode.Allowed));
        _profiles.Current = new ProfileSet(
            [_profiles.Current.Profiles[0], ProfileFactory.Profile(games: ["other"], autoStart: true,
                actions: [new ProfileActionEntry("b", ActionMode.Allowed)], profileId: "second")],
            []);
        StartGame();
        await Coordinator.StartAsync(Ct);
        await WaitForGaming();
        await StopAndWait();

        // Первая игра завершена пользователем и продолжает работать: она не должна заслонять вторую.
        _processes.Start("other", 5151);
        await AdvanceUntil(() => Status.Progress?.State == SessionState.Gaming);

        Assert.Equal("second", Status.Plan?.Profile.ProfileId);
        Assert.Equal(5151, Status.Plan?.Game?.Identity.ProcessId);
        Assert.Equal(2, Applies);
    }

    [Fact]
    public async Task Ignored_identity_is_forgotten_after_the_process_exits()
    {
        UseProfile(autoStart: true, ("b", ActionMode.Allowed));
        StartGame();
        await Coordinator.StartAsync(Ct);
        await WaitForGaming();
        await StopAndWait();
        await Polls(2);

        _processes.Exit(GamePid);
        await Polls(2);
        StartGame(GamePid + 1, Start.AddMinutes(5));
        await AdvanceUntil(() => Status.Progress?.State == SessionState.Gaming);

        Assert.Equal(GamePid + 1, Status.Plan?.Game?.Identity.ProcessId);
        Assert.Equal(2, Applies);
    }

    [Fact]
    public async Task Nothing_to_apply_is_not_retried_every_poll()
    {
        UseProfile(autoStart: true, ("a", ActionMode.AskFirst));
        StartGame();
        await Coordinator.StartAsync(Ct);

        await Polls(5);

        Assert.Equal(1, _actions["a"].Checks);
        Assert.Equal(0, Applies);
        Assert.Empty(_journal.Saves);
        Assert.Equal(CoordinatorMode.Watching, Status.Mode);
        Assert.Null(Status.LastSession);
    }

    [Fact]
    public async Task Session_start_failure_is_logged_and_not_retried_for_the_process()
    {
        UseProfile(autoStart: true, ("b", ActionMode.Allowed));
        _journal.FailWith = _ => new IOException("disk full");
        StartGame();
        await Coordinator.StartAsync(Ct);

        await Polls(5);

        Assert.Equal(1, _actions["b"].Checks);
        Assert.Equal(0, Applies);
        Assert.Equal(CoordinatorMode.Watching, Status.Mode);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task StopAsync_restores_active_session_and_is_idempotent()
    {
        UseProfile(autoStart: true, ("b", ActionMode.Allowed));
        StartGame();
        await Coordinator.StartAsync(Ct);
        await WaitForGaming();
        Assert.Equal("boost", _state["b"]);

        await Coordinator.StopAsync(Ct).WaitAsync(Wait, Ct);

        Assert.Equal("orig", _state["b"]);
        Assert.Equal(SessionOutcome.Completed, Status.LastSession?.Outcome);
        Assert.Equal(CoordinatorMode.Off, Status.Mode);
        var calls = _state.Calls;
        var polls = _processes.ListCalls;

        await Coordinator.StopAsync(Ct).WaitAsync(Wait, Ct);
        await Idle(3);

        Assert.Equal(calls, _state.Calls);
        Assert.Equal(polls, _processes.ListCalls);
    }

    [Fact]
    public async Task Earliest_started_process_is_the_game()
    {
        UseProfile(autoStart: true, ("b", ActionMode.Allowed));
        StartGame(100, Start.AddSeconds(30));
        StartGame(200, Start);
        StartGame(300, Start.AddSeconds(10));
        await Coordinator.StartAsync(Ct);

        await WaitForGaming();

        Assert.Equal(new ProcessIdentity(200, Start), Status.Plan?.Game?.Identity);
        Assert.Equal(200, _catalog.LastGame?.Identity.ProcessId);
    }

    [Fact]
    public async Task Disarm_prevents_start()
    {
        UseProfile(autoStart: false, ("a", ActionMode.AskFirst));
        await Coordinator.StartAsync(Ct);
        await Coordinator.ArmAsync(ProfileId, Confirmed("a"), Ct);
        await WaitFor(() => _processes.ListCalls > 0);

        Coordinator.Disarm();
        StartGame();
        await Idle(5);

        Assert.Equal(CoordinatorMode.Off, Status.Mode);
        Assert.Null(Status.ArmedProfileId);
        Assert.Equal(0, Applies);

        // Контроль: та же игра с новой подготовкой запускает сессию.
        await Coordinator.ArmAsync(ProfileId, Confirmed("a"), Ct);
        await WaitForGaming();
    }

    [Fact]
    public async Task Arm_after_stopped_session_starts_again_for_the_running_game()
    {
        UseProfile(autoStart: true, ("b", ActionMode.Allowed));
        StartGame();
        await Coordinator.StartAsync(Ct);
        await WaitForGaming();
        await StopAndWait();

        await Coordinator.ArmAsync(ProfileId, Confirmed(), Ct);
        await WaitForGaming();

        Assert.Equal(ExecutionTrigger.UserInitiated, Status.Plan?.Trigger);
        Assert.Equal(2, Applies);
    }

    [Fact]
    public async Task Arm_needs_a_known_profile_with_games_and_no_running_session()
    {
        _profiles.Current = new ProfileSet([ProfileFactory.Profile(games: [])], []);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Coordinator.ArmAsync(ProfileId, Confirmed(), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => Coordinator.ArmAsync("unknown", Confirmed(), Ct));

        UseProfile(autoStart: true, ("b", ActionMode.Allowed));
        StartGame();
        await Coordinator.StartAsync(Ct);
        await WaitForGaming();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Coordinator.ArmAsync(ProfileId, Confirmed(), Ct));
    }

    [Fact]
    public async Task Preview_lists_actions_needing_confirmation()
    {
        UseProfile(autoStart: false, ("a", ActionMode.AskFirst), ("b", ActionMode.Allowed));

        var plan = await Coordinator.PreviewAsync(ProfileId, Ct);

        Assert.Null(plan.Game);
        Assert.Equal(ExecutionTrigger.UserInitiated, plan.Trigger);
        var a = plan.Actions.Single(p => p.Action.Id == "a");
        Assert.False(a.Context.IsGamingSessionActive);
        Assert.Equal(PolicyVerdict.RequireConfirmation, a.Decision.Verdict);
        Assert.Equal(PolicyVerdict.Allow, plan.Actions.Single(p => p.Action.Id == "b").Decision.Verdict);
        Assert.Equal(0, Applies);
        await Assert.ThrowsAsync<ArgumentException>(() => Coordinator.PreviewAsync("unknown", Ct));
    }

    [Fact]
    public async Task Status_changes_are_announced()
    {
        UseProfile(autoStart: true, ("b", ActionMode.Allowed));
        var modes = new List<CoordinatorMode>();
        Coordinator.StatusChanged += (_, _) =>
        {
            lock (modes) modes.Add(Coordinator.Status.Mode);
        };
        StartGame();
        await Coordinator.StartAsync(Ct);

        await WaitForGaming();
        Coordinator.StopSession();

        await WaitFor(() =>
        {
            lock (modes) return modes.Contains(CoordinatorMode.InSession) && modes[^1] == CoordinatorMode.Watching;
        });
    }
}
