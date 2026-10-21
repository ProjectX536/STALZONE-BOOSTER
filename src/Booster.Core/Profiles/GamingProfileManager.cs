using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Engine;
using Booster.Core.Recovery;
using Booster.Core.Reports;
using Booster.Core.Resources;
using Booster.Core.Sensors;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Profiles;

public sealed partial class GamingProfileManager(
    IActionCatalog catalog,
    IActionPolicy policy,
    ActionExecutor executor,
    RollbackManager rollback,
    RecoveryValidator validator,
    IChangeJournal journal,
    IProcessProvider processes,
    ISystemLoadSource load,
    ISensorProvider sensors,
    SessionFrameCapture frames,
    IBoosterConfiguration config,
    TimeProvider time,
    ILogger<GamingProfileManager> logger)
{
    private int _running;
    private volatile SessionState _state = SessionState.Idle;

    public SessionState State => _state;

    public event EventHandler<SessionProgress>? ProgressChanged;

    public async Task<SessionPlan> PreflightAsync(GamingProfile profile, GameProcess? game, ExecutionTrigger trigger,
        IReadOnlySet<string> confirmedActionIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(confirmedActionIds);

        var actions = new List<PlannedAction>();
        foreach (var entry in profile.Actions)
        {
            ct.ThrowIfCancellationRequested();
            if (catalog.Create(entry.Id, profile, game) is not { } action)
                continue;

            var context = new PolicyContext(trigger, IsGamingSessionActive: game is not null,
                UserEnabled: profile.EnabledOptionalActions.Contains(entry.Id, StringComparer.OrdinalIgnoreCase),
                UserConfirmed: confirmedActionIds.Contains(entry.Id), CanElevate: false);

            var support = game is not null && !action.Descriptor.IsReversible
                ? new ActionSupport(false, ReportStrings.Skip_OnlyBeforeGame, action.Descriptor.DisplayName)
                : await CheckSupportAsync(action, ct).ConfigureAwait(false);
            actions.Add(new PlannedAction(action, entry.Mode, context, support, policy.Evaluate(action.Descriptor, entry.Mode, context)));
        }

        return new SessionPlan(profile, game, trigger, actions);
    }

    public async Task<SessionRecord?> RunAsync(SessionPlan plan, CancellationToken stopToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Game is not { } game)
            throw new ArgumentException("A gaming session needs a running game", nameof(plan));
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            throw new InvalidOperationException("A gaming session is already running");

        try
        {
            if (!plan.Actions.Any(a => a.WillApply))
            {
                LogNothingToApply(plan.Profile.ProfileId, game.Name);
                return null;
            }

            return await RunSessionAsync(plan, SessionKind.Game, game, stopToken).ConfigureAwait(false);
        }
        finally
        {
            _state = SessionState.Idle;
            Volatile.Write(ref _running, 0);
        }
    }

    public async Task<SessionRecord?> PrepareAsync(GamingProfile profile, IReadOnlySet<string> confirmedActionIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(confirmedActionIds);
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            throw new InvalidOperationException("A gaming session is already running");

        try
        {
            var plan = await PreflightAsync(profile, null, ExecutionTrigger.UserInitiated, confirmedActionIds, ct).ConfigureAwait(false);
            plan = plan with { Actions = plan.Actions.Where(a => !a.Action.Descriptor.IsReversible).ToArray() };
            if (!plan.Actions.Any(a => a.WillApply))
            {
                LogNothingToPrepare(profile.ProfileId);
                return null;
            }

            return await RunSessionAsync(plan, SessionKind.Preparation, null, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _state = SessionState.Idle;
            Volatile.Write(ref _running, 0);
        }
    }

    private async Task<SessionRecord> RunSessionAsync(SessionPlan plan, SessionKind kind, GameProcess? game, CancellationToken stopToken)
    {
        var profile = plan.Profile;
        var now = time.GetUtcNow();
        var skipped = plan.Actions.Where(a => !a.WillApply).Select(a => Skip(a.Action, a.SkipReason)).ToArray();
        var gameName = game?.Name ?? string.Join(", ", profile.Games);
        var session = new SessionRecord(SessionRecord.CurrentSchemaVersion, SessionIds.New(now), profile.ProfileId, profile.Name,
            gameName, game?.Identity.ProcessId, plan.Trigger, now, SessionState.Snapshot, null, null, [], skipped, kind);
        LogStarted(session.SessionId, kind, profile.ProfileId, gameName, game?.Identity.ProcessId);

        var gameRunning = game is not null;
        session = await EnterAsync(session, SessionState.Snapshot, gameRunning, required: true).ConfigureAwait(false);
        session = await EnterAsync(session, SessionState.Apply, gameRunning, required: true).ConfigureAwait(false);

        var failed = false;
        var toApply = plan.Actions.Where(a => a.WillApply).ToArray();
        for (var i = 0; i < toApply.Length; i++)
        {
            if (stopToken.IsCancellationRequested)
            {
                LogStopRequested(session.SessionId);
                session = session with
                {
                    Skipped = [.. session.Skipped, .. toApply[i..].Select(a => Skip(a.Action, ReportStrings.Skip_SessionStopped))],
                };
                break;
            }

            var planned = toApply[i];
            var result = await executor.ExecuteAsync(session, planned.Action, planned.Support.Summary, planned.Mode, planned.Context,
                CancellationToken.None).ConfigureAwait(false);
            session = result.Session;
            if (result.Status == ExecutionStatus.Failed)
            {
                LogApplyFailed(session.SessionId, planned.Action.Id);
                failed = true;
                session = session with
                {
                    Skipped = [.. session.Skipped, .. toApply[(i + 1)..].Select(a => Skip(a.Action, ReportStrings.Skip_NotAttemptedAfterFailure))],
                };
                break;
            }

            if (result.Status is ExecutionStatus.Refused or ExecutionStatus.NotApplied)
            {
                var reason = result.Status == ExecutionStatus.Refused ? EnumText.Format(result.Decision.Reason) : result.Detail ?? "";
                session = session with { Skipped = [.. session.Skipped, Skip(planned.Action, reason)] };
                await SaveQuietlyAsync(session).ConfigureAwait(false);
            }

            Report(session, null, gameRunning);
        }

        if (game is not null && !failed && !stopToken.IsCancellationRequested)
            (session, gameRunning) = await PlayAsync(plan, game, session, stopToken).ConfigureAwait(false);

        session = await EnterAsync(session, SessionState.Restore, gameRunning, required: false).ConfigureAwait(false);
        session = await rollback.RestoreAsync(session, CancellationToken.None).ConfigureAwait(false);
        session = await EnterAsync(session, SessionState.Verify, gameRunning, required: false).ConfigureAwait(false);
        session = await validator.VerifyAsync(session, CancellationToken.None).ConfigureAwait(false);

        var outcome = failed ? SessionOutcome.RolledBack : SessionOutcome.Completed;
        session = session with { Outcome = outcome, FinishedAt = time.GetUtcNow() };
        session = await EnterAsync(session, SessionState.Idle, gameRunning, required: false).ConfigureAwait(false);
        LogFinished(session.SessionId, outcome);
        return session;
    }

    private async Task<(SessionRecord Session, bool GameRunning)> PlayAsync(SessionPlan plan, GameProcess game, SessionRecord session,
        CancellationToken stopToken)
    {
        var profile = plan.Profile;
        var running = true;
        var misses = 0;
        var exitConfirmations = config.Current.Settings.Monitoring.GameExitConfirmationCount;
        var interval = TimeSpan.FromMilliseconds(profile.Monitoring.SampleIntervalMs);
        var readSensors = profile.Monitoring is { Enabled: true, CollectGpuTelemetry: true };
        var throttleSettings = config.Current.Thresholds.Throttle;
        var window = new Queue<SensorSnapshot>();
        ThrottleAssessment? throttle = null;
        await using var frameRun = frames.Start(profile.Monitoring, game, stopToken);
        using var timer = new PeriodicTimer(interval, time);

        SampleLoad(profile.Monitoring);
        session = await EnterAsync(session, SessionState.Gaming, running, required: false).ConfigureAwait(false);
        try
        {
            while (await timer.WaitForNextTickAsync(stopToken).ConfigureAwait(false))
            {
                if (running)
                {
                    misses = processes.Identify(game.Identity.ProcessId) == game.Identity ? 0 : misses + 1;
                    if (misses >= exitConfirmations)
                    {
                        running = false;
                        LogGameExited(session.SessionId, game.Name);
                    }
                }

                SensorSnapshot? sample = null;
                if (readSensors && await ReadSensorsAsync(session.SessionId, interval, stopToken).ConfigureAwait(false) is { } read)
                {
                    sample = read;
                    window.Enqueue(read);
                    while (window.Count > throttleSettings.SessionWindowSamples)
                        window.Dequeue();
                    throttle = ThermalThrottleDetector.Assess([.. window], throttleSettings);
                }

                Report(session, SampleLoad(profile.Monitoring), running, sample, throttle, frameRun.Summary);
                if (!running && profile.Recovery.RestoreOnGameExit)
                    break;
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            LogStopRequested(session.SessionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogGamingFailed(ex, session.SessionId);
        }

        return (session, running);
    }

    private async Task<SensorSnapshot?> ReadSensorsAsync(string sessionId, TimeSpan limit, CancellationToken stopToken)
    {
        using var abandon = CancellationTokenSource.CreateLinkedTokenSource(stopToken);
        try
        {
            // Task.Run: нативный вызов может зависнуть синхронно, таймаут должен это покрывать.
            return await Task.Run(() => sensors.ReadAsync(abandon.Token), abandon.Token).WaitAsync(limit, time, stopToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            LogSensorReadTimedOut(sessionId, (long)limit.TotalMilliseconds);
            return null;
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && stopToken.IsCancellationRequested))
        {
            LogSensorReadFailed(ex, sessionId);
            return null;
        }
        finally
        {
            await abandon.CancelAsync().ConfigureAwait(false);
        }
    }

    private SystemLoad? SampleLoad(ProfileMonitoring monitoring)
    {
        if (!monitoring.Enabled || !(monitoring.CollectCpuMetrics || monitoring.CollectMemoryMetrics))
            return null;

        var sample = load.Sample();
        return new SystemLoad(monitoring.CollectCpuMetrics ? sample.CpuPercent : null,
            monitoring.CollectMemoryMetrics ? sample.MemoryLoadPercent : null);
    }

    private async Task<ActionSupport> CheckSupportAsync(IReversibleAction action, CancellationToken ct)
    {
        try
        {
            return await action.CheckSupportAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogSupportCheckFailed(ex, action.Id);
            return new ActionSupport(false, ex.Message, action.Descriptor.DisplayName);
        }
    }

    private async Task<SessionRecord> EnterAsync(SessionRecord session, SessionState state, bool gameRunning, bool required)
    {
        session = session with { State = state };
        _state = state;
        if (required)
            await journal.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
        else
            await SaveQuietlyAsync(session).ConfigureAwait(false);
        Report(session, null, gameRunning);
        return session;
    }

    private async Task SaveQuietlyAsync(SessionRecord session)
    {
        try
        {
            await journal.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSaveFailed(ex, session.SessionId, session.State);
        }
    }

    private void Report(SessionRecord session, SystemLoad? sample, bool gameRunning, SensorSnapshot? sensors = null,
        ThrottleAssessment? throttle = null, SessionFrameSummary? frames = null)
    {
        try
        {
            ProgressChanged?.Invoke(this, new SessionProgress(_state, session, sample, gameRunning, sensors, throttle, frames));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogProgressHandlerFailed(ex, session.SessionId);
        }
    }

    private static SkippedAction Skip(IReversibleAction action, string reason) => new(action.Id, action.Descriptor.DisplayName, reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Support check of action {ActionId} failed")]
    private partial void LogSupportCheckFailed(Exception exception, string actionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Profile {ProfileId}: nothing to apply for {Game}")]
    private partial void LogNothingToApply(string profileId, string game);

    [LoggerMessage(Level = LogLevel.Information, Message = "Profile {ProfileId}: nothing to prepare before the game")]
    private partial void LogNothingToPrepare(string profileId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Session {SessionId} ({Kind}) started: profile {ProfileId}, game {Game} (PID {ProcessId})")]
    private partial void LogStarted(string sessionId, SessionKind kind, string profileId, string game, int? processId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Session {SessionId}: action {ActionId} failed, rolling back")]
    private partial void LogApplyFailed(string sessionId, string actionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Session {SessionId}: game {Game} exited")]
    private partial void LogGameExited(string sessionId, string game);

    [LoggerMessage(Level = LogLevel.Information, Message = "Session {SessionId}: stop requested")]
    private partial void LogStopRequested(string sessionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Session {SessionId}: game monitoring failed, restoring")]
    private partial void LogGamingFailed(Exception exception, string sessionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Session {SessionId}: sensor read took longer than {LimitMs} ms, sample skipped")]
    private partial void LogSensorReadTimedOut(string sessionId, long limitMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Session {SessionId}: sensor read failed, sample skipped")]
    private partial void LogSensorReadFailed(Exception exception, string sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Session {SessionId} finished: {Outcome}")]
    private partial void LogFinished(string sessionId, SessionOutcome outcome);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Journal of session {SessionId} not saved in state {State}")]
    private partial void LogSaveFailed(Exception exception, string sessionId, SessionState state);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Session {SessionId}: progress handler failed")]
    private partial void LogProgressHandlerFailed(Exception exception, string sessionId);
}
