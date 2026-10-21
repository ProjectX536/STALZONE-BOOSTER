using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Booster.Core.Metrics;
using Booster.Core.Profiles;
using Booster.Core.Profiles.Actions;
using Booster.Core.Recovery;
using Booster.Core.Reports;
using Booster.Core.Resources;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static Booster.Core.Tests.TestSupport.SessionFactory;

namespace Booster.Core.Tests.Profiles;

public class GamingProfileManagerTests
{
    private const int GamePid = 4242;

    /// <summary>ProfileFactory: sampleIntervalMs = 2000.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly FakeSystemState _state = new();
    private readonly RecordingJournal _journal = new();
    private readonly FakeTimeProvider _time = new(Start);
    private readonly FakeActionCatalog _catalog = new();
    private readonly FakeProcessProvider _processes = new();
    private readonly FakeSystemLoad _load = new();
    private readonly FakeSensorProvider _sensors;
    private readonly FakeFrameCaptureProvider _frames;
    private readonly ListLogger<GamingProfileManager> _log = new();
    private readonly ListLogger<SessionFrameCapture> _frameLog = new();
    private readonly List<IActionRestorer> _restorers = [];
    private readonly List<SessionProgress> _progress = [];
    private FakeConfiguration _config = new();
    private GamingProfileManager? _manager;

    public GamingProfileManagerTests()
    {
        _sensors = new FakeSensorProvider(_time);
        _frames = new FakeFrameCaptureProvider(_time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Создаётся при первом обращении, когда действия и восстановители уже добавлены.</summary>
    private GamingProfileManager Manager => _manager ??= CreateManager();

    private GamingProfileManager CreateManager()
    {
        var policy = new ActionPolicy();
        var manager = new GamingProfileManager(_catalog, policy,
            new ActionExecutor(policy, _journal, _time, NullLogger<ActionExecutor>.Instance),
            new RollbackManager(_restorers, _journal, _time, NullLogger<RollbackManager>.Instance),
            new RecoveryValidator(_restorers, _journal, _time, NullLogger<RecoveryValidator>.Instance),
            _journal, _processes, _load, _sensors, new SessionFrameCapture(_frames, _config, _frameLog), _config, _time, _log);
        manager.ProgressChanged += (_, progress) =>
        {
            lock (_progress) _progress.Add(progress);
        };
        return manager;
    }

    private SessionProgress[] Progress
    {
        get { lock (_progress) return _progress.ToArray(); }
    }

    /// <summary>Смены состояния по событиям ProgressChanged, без повторов подряд.</summary>
    private SessionState[] States()
    {
        var states = new List<SessionState>();
        foreach (var progress in Progress)
        {
            if (states.Count == 0 || states[^1] != progress.State)
                states.Add(progress.State);
        }

        return states.ToArray();
    }

    private FakeAction Action(string id)
    {
        _state[id] = "orig";
        var action = new FakeAction(_state, id);
        _catalog.Add(action);
        _restorers.Add(new FakeRestorer(_state, id));
        return action;
    }

    /// <summary>Необратимое действие с подтверждением, как закрытие фоновых приложений. Политика пустила бы его и в игре.</summary>
    private FakeAction Irreversible(string id)
    {
        _state[id] = "orig";
        var action = new FakeAction(_state, id)
        {
            Descriptor = new ActionDescriptor(id, $"Действие {id}", SafetyLevel.L2Confirmed, RequiresAdmin: false, RequiresReboot: false,
                RequiresConfirmation: true, IsReversible: false, AllowedDuringGaming: true, VerifiedForAutomatic: false),
        };
        _catalog.Add(action);
        return action;
    }

    private static IReadOnlySet<string> Confirmed(params string[] ids) => ids.ToHashSet(StringComparer.Ordinal);

    private GameProcess Game() => new("cs2", _processes.Start("cs2", GamePid));

    private static GamingProfile Profile(params string[] allowedIds) =>
        ProfileFactory.Profile(games: ["cs2"], actions: allowedIds.Select(id => new ProfileActionEntry(id, ActionMode.Allowed)).ToArray());

    private Task<SessionPlan> Preflight(GamingProfile profile, GameProcess? game,
        ExecutionTrigger trigger = ExecutionTrigger.Automatic, params string[] confirmed) =>
        Manager.PreflightAsync(profile, game, trigger, confirmed.ToHashSet(StringComparer.Ordinal), Ct);

    private async Task WaitFor(Func<SessionProgress[], bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(Wait);
        while (!condition(Progress))
            await Task.Delay(10, timeout.Token);
    }

    private Task WaitForGaming(int events = 1) =>
        WaitFor(progress => progress.Count(p => p.State == SessionState.Gaming) >= events);

    /// <summary>
    /// Ещё <paramref name="ticks"/> замеров в Gaming. Время сдвигается на интервал только после события предыдущего замера:
    /// PeriodicTimer сливает тики, пропущенные за один сдвиг.
    /// </summary>
    private async Task Ticks(int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            var seen = Progress.Count(p => p.State == SessionState.Gaming);
            _time.Advance(Interval);
            await WaitForGaming(seen + 1);
        }
    }

    /// <summary>Сессия доходит до Gaming, затем «Завершить сессию».</summary>
    private async Task<SessionRecord?> RunAndStop(SessionPlan plan)
    {
        using var stop = new CancellationTokenSource();
        var run = Manager.RunAsync(plan, stop.Token);
        await WaitForGaming();
        await stop.CancelAsync();
        return await run.WaitAsync(Wait, Ct);
    }

    [Fact]
    public async Task Game_session_never_contains_irreversible_actions()
    {
        Action("a");
        var close = Irreversible("close");
        var profile = ProfileFactory.Profile(games: ["cs2"], actions: [new("a", ActionMode.AskFirst), new("close", ActionMode.AskFirst)]);
        var plan = await Preflight(profile, Game(), ExecutionTrigger.UserInitiated, "a", "close");

        var planned = plan.Actions.Single(p => p.Action.Id == "close");
        Assert.False(planned.WillApply);
        Assert.Equal(ReportStrings.Skip_OnlyBeforeGame, planned.SkipReason);
        Assert.Equal(0, close.Checks);

        var session = await RunAndStop(plan);

        Assert.Equal(["a"], session!.Changes.Select(c => c.ActionId));
        Assert.Contains(session.Skipped, s => s.ActionId == "close" && s.Reason == ReportStrings.Skip_OnlyBeforeGame);
        Assert.Equal(0, close.Applies);
    }

    [Fact]
    public async Task Preparation_closes_apps_and_finishes_with_manual_check()
    {
        var closer = new FakeAppCloser();
        _processes.Start("chrome", 100);
        _processes.Start("Discord", 104);
        Action("a");
        var profile = ProfileFactory.Profile(games: ["cs2", "dota2"], userSelected: ["chrome", "Discord"],
            actions: [new("a", ActionMode.AskFirst), new(ActionIds.BackgroundApps, ActionMode.AskFirst, Reversible: false)]);
        _catalog.Add(new BackgroundAppsAction(_processes, closer, profile, null, _time));

        var session = await Manager.PrepareAsync(profile, Confirmed("a", ActionIds.BackgroundApps), Ct);

        Assert.NotNull(session);
        Assert.Equal(SessionKind.Preparation, session.Kind);
        Assert.Equal("cs2, dota2", session.GameName);
        Assert.Null(session.GameProcessId);
        Assert.Equal([SessionState.Snapshot, SessionState.Apply, SessionState.Restore, SessionState.Verify, SessionState.Idle], States());
        Assert.DoesNotContain(Progress, p => p.GameRunning);
        var change = Assert.Single(session.Changes);
        Assert.Equal(ActionIds.BackgroundApps, change.ActionId);
        Assert.Equal(ChangeStatus.NeedsManualCheck, change.Status);
        Assert.Equal(SessionOutcome.Completed, session.Outcome);
        Assert.Equal(session, _journal.Last);
        Assert.Equal(["chrome", "Discord"], closer.Requested);

        // Обратимые действия ждут игры: подготовка их не применяет.
        Assert.Equal("orig", _state["a"]);
        Assert.Equal(SessionState.Idle, Manager.State);
    }

    [Fact]
    public async Task Preparation_without_irreversible_actions_writes_no_journal()
    {
        Action("a");
        var close = Irreversible("close");
        var profile = ProfileFactory.Profile(games: ["cs2"], actions: [new("a", ActionMode.AskFirst), new("close", ActionMode.AskFirst)]);

        var session = await Manager.PrepareAsync(profile, Confirmed("a"), Ct);

        Assert.Null(session);
        Assert.Empty(_journal.Saves);
        Assert.Equal(0, close.Applies);
        Assert.Equal("orig", _state["a"]);
    }

    [Fact]
    public async Task Apply_failure_rolls_back_applied_actions_in_reverse_order()
    {
        Action("a");
        Action("b");
        var c = Action("c");
        c.ThrowOnApply = true;
        var plan = await Preflight(Profile("a", "b", "c"), Game());

        var session = await Manager.RunAsync(plan, Ct).WaitAsync(Wait, Ct);

        // FakeAction записывает попытку apply:c до исключения; c не откатывается — его значение не менялось.
        Assert.Equal(["apply:a", "apply:b", "apply:c", "revert:b", "revert:a"], _state.Calls);
        Assert.NotNull(session);
        Assert.Equal(["a", "b", "c"], session.Changes.Select(change => change.ActionId));
        Assert.All(session.Changes, change => Assert.Equal(ChangeStatus.Restored, change.Status));
        Assert.Equal("orig", _state["c"]);
        Assert.Equal(SessionOutcome.RolledBack, session.Outcome);
        Assert.Equal(
            [SessionState.Snapshot, SessionState.Apply, SessionState.Restore, SessionState.Verify, SessionState.Idle], States());
        Assert.Equal(session, _journal.Last);
    }

    [Fact]
    public async Task Stop_during_apply_skips_actions_not_started()
    {
        using var stop = new CancellationTokenSource();
        var a = Action("a");
        Action("b");
        Action("c");
        a.OnApply = stop.Cancel;
        var plan = await Preflight(Profile("a", "b", "c"), Game());

        var session = await Manager.RunAsync(plan, stop.Token).WaitAsync(Wait, Ct);

        // Начатое действие доводится до конца и восстанавливается; следующие не начинаются.
        Assert.Equal(["apply:a", "revert:a"], _state.Calls);
        Assert.NotNull(session);
        Assert.Equal(["a"], session.Changes.Select(change => change.ActionId));
        Assert.Equal(["b", "c"], session.Skipped.Select(skip => skip.ActionId));
        Assert.All(session.Skipped, skip => Assert.Equal(ReportStrings.Skip_SessionStopped, skip.Reason));
        Assert.Equal(ChangeStatus.Restored, session.Changes[0].Status);
        Assert.Equal(SessionOutcome.Completed, session.Outcome);
        Assert.DoesNotContain(SessionState.Gaming, States());
        Assert.Equal(SessionState.Idle, Manager.State);
        Assert.Equal(session, _journal.Last);
    }

    [Fact]
    public async Task Session_file_exists_before_first_change()
    {
        var a = Action("a");
        SessionRecord? onDisk = null;
        a.OnApply = () => onDisk = _journal.Last;
        var plan = await Preflight(Profile("a"), Game());

        await RunAndStop(plan);

        Assert.NotNull(onDisk);
        var entry = Assert.Single(onDisk.Changes);
        Assert.Equal("a", entry.ActionId);
        Assert.Equal(ChangeStatus.Pending, entry.Status);
        Assert.Equal(SessionState.Apply, onDisk.State);
    }

    [Fact]
    public async Task Completed_session_restores_and_verifies()
    {
        Action("a");
        Action("b");
        var plan = await Preflight(Profile("a", "b"), Game());
        using var stop = new CancellationTokenSource();

        var run = Manager.RunAsync(plan, stop.Token);
        await WaitForGaming();
        Assert.Equal(SessionState.Gaming, Manager.State);
        Assert.Equal("boost", _state["a"]);
        Assert.Equal("boost", _state["b"]);
        _time.Advance(Interval);
        await WaitForGaming(2);
        Assert.False(run.IsCompleted);
        Assert.True(Progress[^1].GameRunning);
        await stop.CancelAsync();
        var session = await run.WaitAsync(Wait, Ct);

        Assert.Equal(
            [SessionState.Snapshot, SessionState.Apply, SessionState.Gaming, SessionState.Restore, SessionState.Verify, SessionState.Idle],
            States());
        Assert.NotNull(session);
        Assert.All(session.Changes, change =>
        {
            Assert.Equal(ChangeStatus.Restored, change.Status);
            Assert.True(change.Verified);
        });
        Assert.Equal(SessionOutcome.Completed, session.Outcome);
        Assert.Equal(SessionState.Idle, session.State);
        Assert.Equal(_time.GetUtcNow(), session.FinishedAt);
        Assert.True(SessionIds.IsValid(session.SessionId));
        Assert.Equal(("competitive", "Competitive Gaming", "cs2", GamePid, ExecutionTrigger.Automatic, Start),
            (session.ProfileId, session.ProfileName, session.GameName, session.GameProcessId, session.Trigger, session.StartedAt));
        Assert.Equal(session, _journal.Last);
        Assert.Equal(SessionState.Idle, Manager.State);
        Assert.Equal("orig", _state["a"]);
        Assert.Equal("orig", _state["b"]);
    }

    [Fact]
    public async Task Game_exit_ends_gaming()
    {
        Action("a");
        var plan = await Preflight(Profile("a"), Game());

        var run = Manager.RunAsync(plan, Ct);
        await WaitForGaming();
        _processes.Exit(GamePid);
        await Ticks(1);
        _time.Advance(Interval);
        var session = await run.WaitAsync(Wait, Ct);

        Assert.NotNull(session);
        Assert.Equal(SessionOutcome.Completed, session.Outcome);
        Assert.Equal("orig", _state["a"]);
        Assert.Contains(Progress, p => p.State == SessionState.Gaming && !p.GameRunning);
    }

    [Fact]
    public async Task Same_pid_with_other_start_time_counts_as_exit()
    {
        Action("a");
        var plan = await Preflight(Profile("a"), Game());

        var run = Manager.RunAsync(plan, Ct);
        await WaitForGaming();
        _processes.Exit(GamePid);
        _processes.Start("notepad", GamePid, Start.AddMinutes(1));
        await Ticks(1);
        _time.Advance(Interval);
        var session = await run.WaitAsync(Wait, Ct);

        Assert.Equal(SessionOutcome.Completed, session?.Outcome);
        Assert.Equal("orig", _state["a"]);
    }

    [Fact]
    public async Task One_failed_identity_check_does_not_end_the_session()
    {
        Action("a");
        var plan = await Preflight(Profile("a"), Game());
        using var stop = new CancellationTokenSource();

        var run = Manager.RunAsync(plan, stop.Token);
        await WaitForGaming();
        _processes.DenyRead(GamePid);
        await Ticks(1);
        _processes.AllowRead(GamePid);
        await Ticks(2);

        Assert.False(run.IsCompleted);
        Assert.Equal(SessionState.Gaming, Manager.State);
        Assert.All(Progress, p => Assert.True(p.GameRunning));
        Assert.Equal("boost", _state["a"]);
        await stop.CancelAsync();
        await run.WaitAsync(Wait, Ct);
    }

    [Fact]
    public async Task Session_ends_after_the_configured_number_of_misses()
    {
        _config = new FakeConfiguration(gameExitConfirmationCount: 3);
        Action("a");
        var plan = await Preflight(Profile("a"), Game());

        var run = Manager.RunAsync(plan, Ct);
        await WaitForGaming();
        _processes.DenyRead(GamePid);
        await Ticks(2);
        Assert.All(Progress, p => Assert.True(p.GameRunning));
        Assert.DoesNotContain(Progress, p => p.State == SessionState.Restore);
        _time.Advance(Interval);
        var session = await run.WaitAsync(Wait, Ct);

        Assert.Equal(SessionOutcome.Completed, session?.Outcome);
        Assert.Equal("orig", _state["a"]);
        Assert.Single(Progress, p => p.State == SessionState.Gaming && !p.GameRunning);
    }

    [Fact]
    public async Task Actions_after_a_failed_apply_are_listed_as_skipped()
    {
        Action("a");
        Action("b").ThrowOnApply = true;
        Action("c");
        var plan = await Preflight(Profile("a", "b", "c"), Game());

        var session = await Manager.RunAsync(plan, Ct).WaitAsync(Wait, Ct);

        Assert.NotNull(session);
        Assert.Equal(["a", "b"], session.Changes.Select(change => change.ActionId));
        Assert.Equal(new SkippedAction("c", "Действие c", ReportStrings.Skip_NotAttemptedAfterFailure), Assert.Single(session.Skipped));
        Assert.Equal(session.Skipped, _journal.Last?.Skipped);
        Assert.DoesNotContain("apply:c", _state.Calls);
    }

    [Fact]
    public async Task Not_applied_action_is_skipped_without_rollback()
    {
        Action("a");
        Action("b").ApplyResult = ApplyResult.NotApplied("нет доступа");
        var plan = await Preflight(Profile("a", "b"), Game());
        using var stop = new CancellationTokenSource();

        var run = Manager.RunAsync(plan, stop.Token);
        await WaitForGaming();

        Assert.Equal(SessionState.Gaming, Manager.State);
        var gaming = _journal.Last!;
        var change = Assert.Single(gaming.Changes);
        Assert.Equal(("a", ChangeStatus.Applied), (change.ActionId, change.Status));
        Assert.Equal(new SkippedAction("b", "Действие b", "нет доступа"), Assert.Single(gaming.Skipped));
        Assert.Equal("boost", _state["a"]);
        await stop.CancelAsync();
        var session = await run.WaitAsync(Wait, Ct);

        Assert.Equal(SessionOutcome.Completed, session?.Outcome);
        Assert.Equal(["apply:a", "apply:b", "revert:a"], _state.Calls);
    }

    [Fact]
    public async Task RestoreOnGameExit_false_waits_for_stop()
    {
        Action("a");
        var profile = ProfileFactory.Profile(games: ["cs2"], actions: [new("a", ActionMode.Allowed)],
            recovery: new ProfileRecovery(SnapshotBeforeChanges: true, RestoreOnGameExit: false, RecoverInterruptedSession: true,
                VerifyAfterRestore: true));
        var plan = await Preflight(profile, Game());
        using var stop = new CancellationTokenSource();

        var run = Manager.RunAsync(plan, stop.Token);
        await WaitForGaming();
        _processes.Exit(GamePid);
        await Ticks(2);
        Assert.Contains(Progress, p => p.State == SessionState.Gaming && !p.GameRunning);
        await Ticks(1);

        Assert.False(run.IsCompleted);
        Assert.Equal(SessionState.Gaming, Manager.State);
        Assert.Equal("boost", _state["a"]);
        await stop.CancelAsync();
        var session = await run.WaitAsync(Wait, Ct);
        Assert.Equal(SessionOutcome.Completed, session?.Outcome);
        Assert.Equal("orig", _state["a"]);
    }

    [Fact]
    public async Task Nothing_to_apply_creates_no_journal()
    {
        Action("a");
        var profile = ProfileFactory.Profile(games: ["cs2"], actions: [new("a", ActionMode.AskFirst)]);
        var plan = await Preflight(profile, Game(), ExecutionTrigger.Automatic);

        var session = await Manager.RunAsync(plan, Ct);

        Assert.False(Assert.Single(plan.Actions).WillApply);
        Assert.Null(session);
        Assert.Empty(_journal.Saves);
        Assert.Empty(_state.Calls);
        Assert.Empty(Progress);
        Assert.Equal(SessionState.Idle, Manager.State);
    }

    [Fact]
    public async Task Skipped_actions_are_recorded_with_reason()
    {
        Action("a");
        var b = Action("b");
        b.Support = new ActionSupport(false, "Схемы нет", "b");
        Action("c");
        var profile = ProfileFactory.Profile(games: ["cs2"],
            actions: [new("a", ActionMode.Allowed), new("b", ActionMode.Allowed), new("c", ActionMode.Optional)]);
        var plan = await Preflight(profile, Game());

        var session = await RunAndStop(plan);

        SkippedAction[] expected =
        [
            new("b", "Действие b", "Схемы нет"),
            new("c", "Действие c", EnumText.Format(PolicyReason.OptionalNotEnabled)),
        ];
        Assert.NotNull(session);
        Assert.Equal(expected, session.Skipped);
        Assert.Equal(expected, _journal.Saves[0].Skipped);
        Assert.Equal("a", Assert.Single(session.Changes).ActionId);
        Assert.Equal(["apply:a", "revert:a"], _state.Calls);
    }

    [Fact]
    public async Task Journal_failure_before_first_change_applies_nothing()
    {
        var a = Action("a");
        var plan = await Preflight(Profile("a"), Game());
        _journal.FailWith = _ => new IOException("disk full");

        await Assert.ThrowsAsync<IOException>(() => Manager.RunAsync(plan, Ct));

        Assert.Equal(0, a.Captures);
        Assert.Equal(0, a.Applies);
        Assert.Equal("orig", _state["a"]);
        Assert.Equal(SessionState.Idle, Manager.State);

        _journal.FailWith = null;
        Assert.NotNull(await RunAndStop(plan));
    }

    [Fact]
    public async Task Preflight_changes_nothing()
    {
        var a = Action("a");
        var b = Action("b");
        b.ThrowOnCheck = true;
        var game = Game();
        var profile = ProfileFactory.Profile(games: ["cs2"],
            actions: [new("a", ActionMode.Allowed), new("registry-tweaks", ActionMode.RecommendOnly), new("b", ActionMode.Allowed)]);

        var plan = await Preflight(profile, game);

        Assert.Equal(0, a.Captures + a.Applies + b.Captures + b.Applies);
        Assert.Empty(_journal.Saves);
        Assert.Empty(_state.Calls);
        Assert.Equal(SessionState.Idle, Manager.State);
        Assert.Same(game, _catalog.LastGame);
        Assert.Equal(["a", "b"], plan.Actions.Select(p => p.Action.Id));
        var failed = plan.Actions[1];
        Assert.False(failed.Support.IsSupported);
        Assert.Equal("check failed", failed.Support.Reason);
        Assert.False(failed.WillApply);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("b", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Optional_action_needs_user_enable()
    {
        Action("a");
        ProfileActionEntry[] actions = [new("a", ActionMode.Optional)];

        var off = await Preflight(ProfileFactory.Profile(games: ["cs2"], actions: actions), Game());
        var on = await Preflight(ProfileFactory.Profile(games: ["cs2"], actions: actions, enabledOptional: ["a"]), Game());

        Assert.Equal(new PolicyDecision(PolicyVerdict.NotEnabled, PolicyReason.OptionalNotEnabled), Assert.Single(off.Actions).Decision);
        Assert.False(off.Actions[0].Context.UserEnabled);
        Assert.Equal(PolicyVerdict.Allow, Assert.Single(on.Actions).Decision.Verdict);
        Assert.True(on.Actions[0].Context.UserEnabled);
        Assert.True(on.Actions[0].WillApply);
    }

    [Fact]
    public async Task Context_comes_from_trigger_game_and_confirmation()
    {
        Action("a");
        var profile = ProfileFactory.Profile(games: ["cs2"], actions: [new("a", ActionMode.AskFirst)]);

        var preview = await Preflight(profile, null, ExecutionTrigger.UserInitiated);
        var confirmed = await Preflight(profile, Game(), ExecutionTrigger.UserInitiated, "a");

        Assert.Equal(new PolicyContext(ExecutionTrigger.UserInitiated, IsGamingSessionActive: false, UserEnabled: false,
            UserConfirmed: false, CanElevate: false), preview.Actions[0].Context);
        Assert.Equal(PolicyVerdict.RequireConfirmation, preview.Actions[0].Decision.Verdict);
        Assert.Null(preview.Game);
        Assert.Equal(new PolicyContext(ExecutionTrigger.UserInitiated, IsGamingSessionActive: true, UserEnabled: false,
            UserConfirmed: true, CanElevate: false), confirmed.Actions[0].Context);
        Assert.True(confirmed.Actions[0].WillApply);
    }

    [Fact]
    public async Task Second_run_while_active_throws()
    {
        Action("a");
        var plan = await Preflight(Profile("a"), Game());
        using var stop = new CancellationTokenSource();

        var first = Manager.RunAsync(plan, stop.Token);
        await WaitForGaming();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Manager.RunAsync(plan, Ct));
        await stop.CancelAsync();

        Assert.NotNull(await first.WaitAsync(Wait, Ct));
        Assert.Equal(["apply:a", "revert:a"], _state.Calls);
    }

    [Fact]
    public async Task Run_needs_a_game()
    {
        Action("a");
        var plan = await Preflight(Profile("a"), null);

        await Assert.ThrowsAsync<ArgumentException>(() => Manager.RunAsync(plan, Ct));

        Assert.Empty(_journal.Saves);
    }

    [Fact]
    public async Task Load_is_not_sampled_when_monitoring_disabled()
    {
        Action("a");
        var profile = ProfileFactory.Profile(games: ["cs2"], actions: [new("a", ActionMode.Allowed)], monitoringEnabled: false);
        var plan = await Preflight(profile, Game());
        using var stop = new CancellationTokenSource();

        var run = Manager.RunAsync(plan, stop.Token);
        await WaitForGaming();
        _time.Advance(Interval);
        await WaitForGaming(2);
        await stop.CancelAsync();
        await run.WaitAsync(Wait, Ct);

        Assert.Equal(0, _load.Samples);
        Assert.All(Progress, p => Assert.Null(p.Load));
    }

    [Fact]
    public async Task Load_is_sampled_each_interval_and_unrequested_half_is_null()
    {
        Action("a");
        var profile = Profile("a");
        profile = profile with { Monitoring = profile.Monitoring with { CollectCpuMetrics = false } };
        var plan = await Preflight(profile, Game());
        using var stop = new CancellationTokenSource();

        var run = Manager.RunAsync(plan, stop.Token);
        await WaitForGaming();
        _time.Advance(Interval);
        await WaitForGaming(2);
        await stop.CancelAsync();
        await run.WaitAsync(Wait, Ct);

        Assert.Equal(new SystemLoad(null, 40), Progress.Last(p => p.State == SessionState.Gaming).Load);
    }

    [Fact]
    public async Task Progress_handler_failure_does_not_stop_the_session()
    {
        Action("a");
        var plan = await Preflight(Profile("a"), Game());
        Manager.ProgressChanged += (_, _) => throw new InvalidOperationException("ui failed");

        var session = await RunAndStop(plan);

        Assert.Equal(SessionOutcome.Completed, session?.Outcome);
        Assert.Equal("orig", _state["a"]);
    }

    [Fact]
    public async Task Gpu_telemetry_is_read_only_when_enabled()
    {
        Action("a");
        var profile = Profile("a");
        profile = profile with { Monitoring = profile.Monitoring with { CollectGpuTelemetry = false } };
        var plan = await Preflight(profile, Game());

        using var stop = new CancellationTokenSource();
        var run = Manager.RunAsync(plan, stop.Token);
        await WaitForGaming();
        await Ticks(3);
        await stop.CancelAsync();
        await run.WaitAsync(Wait, Ct);

        Assert.Equal(0, _sensors.Calls);
        Assert.All(Progress, p => Assert.Null(p.Sensors));
    }

    [Fact]
    public async Task Gpu_telemetry_is_reported_when_enabled()
    {
        Action("a");
        var plan = await Preflight(Profile("a"), Game());

        using var stop = new CancellationTokenSource();
        var run = Manager.RunAsync(plan, stop.Token);
        await WaitForGaming();
        await Ticks(2);
        await stop.CancelAsync();
        await run.WaitAsync(Wait, Ct);

        Assert.Equal(2, _sensors.Calls);
        Assert.Contains(Progress, p => p.Sensors is not null && p.Throttle is { SampleCount: 2 });
    }

    [Fact]
    public async Task Hanging_sensor_read_does_not_delay_game_exit_detection()
    {
        Action("a");
        _sensors.HangOnCall = 1;
        var plan = await Preflight(Profile("a"), Game());

        var run = Manager.RunAsync(plan, Ct);
        await WaitForGaming();
        _processes.Exit(GamePid);

        // Выход подтверждается за gameExitConfirmationCount = 2 замера; зависшее чтение ограничено интервалом цикла.
        // Сдвиг на интервал, затем пауза, чтобы цикл успел дойти до следующего ожидания.
        for (var i = 0; i < 6 && !run.IsCompleted; i++)
        {
            _time.Advance(Interval);
            await Task.WhenAny(run, Task.Delay(100, Ct));
        }

        var session = await run.WaitAsync(Wait, Ct);
        Assert.Equal(SessionOutcome.Completed, session!.Outcome);
        Assert.Equal("orig", _state["a"]);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("sensor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Sensor_read_blocking_synchronously_does_not_delay_game_exit_detection()
    {
        Action("a");
        _sensors.BlockOnCall = 1;
        var plan = await Preflight(Profile("a"), Game());

        var run = Manager.RunAsync(plan, Ct);
        await WaitForGaming();
        _processes.Exit(GamePid);

        for (var i = 0; i < 6 && !run.IsCompleted; i++)
        {
            _time.Advance(Interval);
            await Task.WhenAny(run, Task.Delay(100, Ct));
        }

        var session = await run.WaitAsync(Wait, Ct);
        Assert.Equal(SessionOutcome.Completed, session!.Outcome);
        Assert.Equal("orig", _state["a"]);
        Assert.False(_sensors.BlockRanOut, "session loop waited for the blocked sensor read instead of abandoning it");
    }

    [Fact]
    public async Task Throttle_assessment_uses_the_session_window()
    {
        Action("a");
        // Первые 5 замеров — с причиной «перегрев» от драйвера, следующие 30 — без причин: окно в 30 замеров её не видит.
        var reads = 0;
        _sensors.Sample = at => SensorSamples.Snapshot(0, SensorSamples.Telemetry(
            reasons: ++reads <= 5 ? GpuThrottleReasons.Thermal : GpuThrottleReasons.None)) with { TakenAt = at };
        var plan = await Preflight(Profile("a"), Game());

        using var stop = new CancellationTokenSource();
        var run = Manager.RunAsync(plan, stop.Token);
        await WaitForGaming();
        await Ticks(5);
        Assert.NotEmpty(Progress[^1].Throttle!.Findings);
        await Ticks(30);
        await stop.CancelAsync();
        await run.WaitAsync(Wait, Ct);

        var last = Progress.Last(p => p.Throttle is not null).Throttle!;
        Assert.Equal(_config.Current.Thresholds.Throttle.SessionWindowSamples, last.SampleCount);
        Assert.Empty(last.Findings);
    }

    /// <summary>Кадры игры: <paramref name="count"/> интервалов по <paramref name="intervalMs"/> мс.</summary>
    private static FrameCaptureResult Captured(int count, double intervalMs)
    {
        var frames = Enumerable.Range(0, count).Select(i => new FrameSample(i, intervalMs, 5, 6, 0)).ToArray();
        return new FrameCaptureResult(FrameCaptureStatus.Completed,
            new FrameCapture("cs2.exe", GamePid, frames, "MsBetweenPresents", 0, ["Application"], ["Hardware: Independent Flip"], @"C:\c.csv"),
            Reading<AdapterLuid>.Unknown(), null);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(Wait);
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    /// <summary>Замеры, пока на панели не появится состояние замера кадров <paramref name="status"/>: оно приходит с шагом наблюдения.</summary>
    private async Task<SessionFrameSummary> TicksUntilFrames(SessionFrameStatus status)
    {
        for (var i = 0; i < 10 && !Progress.Any(p => p.Frames?.Status == status); i++)
            await Ticks(1);
        return Progress.Last(p => p.Frames?.Status == status).Frames!;
    }

    [Fact]
    public async Task Capture_starts_when_profile_collects_frames_and_capture_is_direct()
    {
        Action("a");
        _frames.Result = _ => Captured(18_000, 1000.0 / 60);
        var plan = await Preflight(Profile("a"), Game());

        using var stop = new CancellationTokenSource();
        var run = Manager.RunAsync(plan, stop.Token);
        await WaitForGaming();
        await WaitUntil(() => _frames.StartedAt is not null);
        await TicksUntilFrames(SessionFrameStatus.Capturing);
        _time.Advance(TimeSpan.FromSeconds(_config.Current.Settings.Benchmark.SessionCaptureSeconds));
        var frames = await TicksUntilFrames(SessionFrameStatus.Completed);
        await stop.CancelAsync();
        await run.WaitAsync(Wait, Ct);

        var request = _frames.LastRequest!;
        Assert.Equal(GamePid, request.ProcessId);
        Assert.Null(request.ProcessName);
        Assert.Equal(TimeSpan.FromSeconds(300), request.Duration);
        Assert.True(request.StopWhenProcessExits);
        Assert.Equal(FrameCapturePurpose.GameSession, request.Purpose);
        Assert.Equal(TimeSpan.FromSeconds(300), frames.Duration);
        Assert.Equal(FrameMetricsStatus.Ok, frames.Metrics!.Status);
        Assert.Equal(60, frames.Metrics.Metrics!.AverageFps, 6);
        Assert.Equal(0, frames.Stutter!.StutterCount);
    }

    [Theory]
    [InlineData(true, FrameCaptureMode.ElevationPrompt, SessionFrameStatus.NeedsElevation)]
    [InlineData(false, FrameCaptureMode.Direct, SessionFrameStatus.NotInstalled)]
    public async Task No_capture_and_needs_elevation_status_when_prompt_required(bool installed, FrameCaptureMode mode,
        SessionFrameStatus expected)
    {
        Action("a");
        _frames.Availability = _frames.Availability with { IsInstalled = installed, Mode = mode };
        var plan = await Preflight(Profile("a"), Game());

        using var stop = new CancellationTokenSource();
        var run = Manager.RunAsync(plan, stop.Token);
        await WaitForGaming();
        await TicksUntilFrames(expected);
        await stop.CancelAsync();
        await run.WaitAsync(Wait, Ct);

        Assert.Equal(0, _frames.Calls);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Profile_without_flag_does_not_capture(bool monitoringEnabled, bool collectFrames)
    {
        Action("a");
        var profile = Profile("a");
        profile = profile with { Monitoring = profile.Monitoring with { Enabled = monitoringEnabled, CollectFrameMetrics = collectFrames } };
        var plan = await Preflight(profile, Game());

        using var stop = new CancellationTokenSource();
        var run = Manager.RunAsync(plan, stop.Token);
        await WaitForGaming();
        await TicksUntilFrames(SessionFrameStatus.Disabled);
        await stop.CancelAsync();
        await run.WaitAsync(Wait, Ct);

        Assert.Equal(0, _frames.Calls);
        Assert.DoesNotContain(Progress, p => p.Frames is { Status: not SessionFrameStatus.Disabled });
    }

    [Fact]
    public async Task Capture_failure_does_not_stop_session()
    {
        Action("a");
        _frames.Failure = new InvalidOperationException("PresentMon broke");
        var plan = await Preflight(Profile("a"), Game());

        using var stop = new CancellationTokenSource();
        var run = Manager.RunAsync(plan, stop.Token);
        await WaitForGaming();
        var frames = await TicksUntilFrames(SessionFrameStatus.Failed);
        await Ticks(2);
        Assert.Equal(SessionState.Gaming, Progress[^1].State);
        await stop.CancelAsync();
        var session = await run.WaitAsync(Wait, Ct);

        Assert.Equal(SessionOutcome.Completed, session!.Outcome);
        Assert.Equal("orig", _state["a"]);
        Assert.Contains("PresentMon broke", frames.Detail, StringComparison.Ordinal);
        Assert.Contains(_frameLog.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Failed_capture_result_is_failed_with_its_reason()
    {
        Action("a");
        _frames.Result = _ => FakeFrameCaptureProvider.Empty;
        var plan = await Preflight(Profile("a"), Game());

        using var stop = new CancellationTokenSource();
        var run = Manager.RunAsync(plan, stop.Token);
        await WaitForGaming();
        await WaitUntil(() => _frames.StartedAt is not null);
        _time.Advance(TimeSpan.FromSeconds(300));
        var frames = await TicksUntilFrames(SessionFrameStatus.Failed);
        await stop.CancelAsync();
        await run.WaitAsync(Wait, Ct);

        Assert.Contains(nameof(FrameCaptureStatus.NoFrames), frames.Detail, StringComparison.Ordinal);
        Assert.Null(frames.Metrics);
    }

    [Fact]
    public async Task Session_end_cancels_capture()
    {
        Action("a");
        var plan = await Preflight(Profile("a"), Game());

        using var stop = new CancellationTokenSource();
        var run = Manager.RunAsync(plan, stop.Token);
        await WaitForGaming();
        await WaitUntil(() => _frames.StartedAt is not null);
        await stop.CancelAsync();
        await run.WaitAsync(Wait, Ct);

        await WaitUntil(() => _frames.WasCancelled);
    }

    [Fact]
    public async Task Game_exit_ends_the_capture_with_the_session()
    {
        Action("a");
        var plan = await Preflight(Profile("a"), Game());

        var run = Manager.RunAsync(plan, Ct);
        await WaitForGaming();
        await WaitUntil(() => _frames.StartedAt is not null);
        _processes.Exit(GamePid);
        for (var i = 0; i < 6 && !run.IsCompleted; i++)
        {
            _time.Advance(Interval);
            await Task.WhenAny(run, Task.Delay(100, Ct));
        }

        await run.WaitAsync(Wait, Ct);
        await WaitUntil(() => _frames.WasCancelled);
    }
}
