using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Engine;
using Booster.Core.Recovery;
using Booster.Core.Resources;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Profiles;

public enum CoordinatorMode
{
    Off,

    Watching,

    Armed,

    InSession,
}

public sealed record CoordinatorStatus(CoordinatorMode Mode, string? ArmedProfileId, SessionPlan? Plan, SessionProgress? Progress,
    SessionRecord? LastSession);

public sealed record PreparationResult(SessionRecord? Preparation, string? SkippedReason);

public sealed partial class GameSessionCoordinator(GamingProfileManager manager, IProcessProvider processes,
    IProfileRepository profiles, IBoosterConfiguration config, TimeProvider time, ILogger<GameSessionCoordinator> logger)
    : IAsyncDisposable
{
    private static readonly IReadOnlySet<string> NoConfirmations = new HashSet<string>(StringComparer.Ordinal);

    private readonly Lock _gate = new();
    private CoordinatorStatus _status = new(CoordinatorMode.Off, null, null, null, null);
    private bool _active;
    private Task? _loop;
    private CancellationTokenSource? _loopCts;
    private Arming? _armed;

    private readonly HashSet<ProcessIdentity> _ignored = [];
    private CancellationTokenSource? _sessionCts;
    private bool _preparing;
    private CancellationTokenSource? _wakeCts;
    private bool _wakeRequested;

    public CoordinatorStatus Status
    {
        get
        {
            lock (_gate) return _status;
        }
    }

    public event EventHandler? StatusChanged;

    public Task StartAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_loop is not null)
                return Task.CompletedTask;

            manager.ProgressChanged += OnProgress;
            _active = true;
            var loopCts = _loopCts = new CancellationTokenSource();
            _loop = Task.Run(() => LoopAsync(loopCts.Token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        Task? loop;
        CancellationTokenSource? loopCts;
        lock (_gate)
        {
            loop = _loop;
            loopCts = _loopCts;
            _active = false;
        }

        if (loop is null || loopCts is null)
            return;

        await loopCts.CancelAsync().ConfigureAwait(false);
        await loop.WaitAsync(ct).ConfigureAwait(false);

        lock (_gate)
        {
            if (_loop != loop)
                return;
            _loop = null;
            _loopCts = null;
            manager.ProgressChanged -= OnProgress;
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));

    public Task<SessionPlan> PreviewAsync(string profileId, CancellationToken ct) =>
        manager.PreflightAsync(Find(profileId), game: null, ExecutionTrigger.UserInitiated, NoConfirmations, ct);

    public async Task<PreparationResult> ArmAsync(string profileId, IReadOnlySet<string> confirmedActionIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(confirmedActionIds);
        var profile = Find(profileId);
        if (profile.Games.Count == 0)
            throw new InvalidOperationException($"Profile '{profile.ProfileId}' has no games to watch for");

        lock (_gate)
        {
            if (_sessionCts is not null || _preparing)
                throw new InvalidOperationException("A gaming session is already running");

            _preparing = true;
        }

        PreparationResult result;
        try
        {
            var confirmed = new HashSet<string>(confirmedActionIds, StringComparer.Ordinal);
            result = ProfileMatcher.MatchAll(processes.List(), [profile]).Count > 0
                ? new PreparationResult(null, await SkippedBecauseGameRunsAsync(profile, confirmed, ct).ConfigureAwait(false))
                : new PreparationResult(await manager.PrepareAsync(profile, confirmed, ct).ConfigureAwait(false), null);

            lock (_gate)
            {
                _armed = new Arming(profile, confirmed);
                _ignored.Clear();
            }
        }
        finally
        {
            lock (_gate) _preparing = false;
        }

        LogArmed(profile.ProfileId);
        Publish(s => s with { Mode = IdleMode(), ArmedProfileId = _armed?.ProfileId });
        Wake();
        return result;
    }

    private async Task<string?> SkippedBecauseGameRunsAsync(GamingProfile profile, IReadOnlySet<string> confirmed, CancellationToken ct)
    {
        var plan = await manager.PreflightAsync(profile, game: null, ExecutionTrigger.UserInitiated, confirmed, ct).ConfigureAwait(false);
        if (!plan.Actions.Any(a => !a.Action.Descriptor.IsReversible && a.WillApply))
            return null;

        LogPreparationSkipped(profile.ProfileId);
        return ReportStrings.Skip_OnlyBeforeGame;
    }

    public void Disarm()
    {
        lock (_gate)
        {
            if (_armed is null)
                return;
            _armed = null;
        }

        LogDisarmed();
        Publish(s => s.Mode == CoordinatorMode.InSession ? s : s with { Mode = IdleMode(), ArmedProfileId = null });
    }

    public void StopSession()
    {
        lock (_gate)
        {
            _ = _sessionCts?.CancelAsync();
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await StepAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogStepFailed(ex);
            }

            await WaitAsync(TimeSpan.FromMilliseconds(config.Current.Settings.Monitoring.GameDetectionIntervalMs), ct)
                .ConfigureAwait(false);
        }

        Publish(s => s with { Mode = CoordinatorMode.Off, Plan = null, Progress = null });
    }

    private async Task StepAsync(CancellationToken ct)
    {
        var candidates = Candidates();
        if (candidates.Count == 0)
            return;

        var running = processes.List();
        Forget(running);
        if (FirstNotIgnored(ProfileMatcher.MatchAll(running, candidates)) is not ({ } match, { } game))
            return;

        Arming? arm;
        CancellationTokenSource sessionCts;
        lock (_gate)
        {
            if (_ignored.Contains(game.Identity) || !_active || _preparing)
                return;
            arm = _armed;
            _armed = null;
            _ignored.Add(game.Identity);
            sessionCts = _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        }

        var trigger = arm is null ? ExecutionTrigger.Automatic : ExecutionTrigger.UserInitiated;
        LogAttempt(match.Profile.ProfileId, game.Name, game.Identity.ProcessId, trigger);
        SessionRecord? session = null;
        try
        {
            var plan = await manager.PreflightAsync(match.Profile, game, trigger, arm?.Confirmed ?? NoConfirmations, ct)
                .ConfigureAwait(false);

            if (sessionCts.IsCancellationRequested)
                return;

            if (plan.Actions.Any(a => a.WillApply))
                Publish(s => s with { Mode = CoordinatorMode.InSession, ArmedProfileId = null, Plan = plan, Progress = null });
            session = await manager.RunAsync(plan, sessionCts.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate) _sessionCts = null;
            sessionCts.Dispose();
            Publish(s => s with
            {
                Mode = IdleMode(),
                ArmedProfileId = _armed?.ProfileId,
                Plan = null,
                Progress = null,
                LastSession = session ?? s.LastSession,
            });
        }
    }

    private IReadOnlyList<GamingProfile> Candidates()
    {
        var set = profiles.Current;
        string? dropped = null;
        IReadOnlyList<GamingProfile> candidates;
        lock (_gate)
        {
            if (_armed is { } arm && set.Find(arm.ProfileId) is not { Games.Count: > 0 })
            {
                dropped = arm.ProfileId;
                _armed = null;
            }

            candidates = _armed is { } armed
                ? [armed.Profile]
                : set.Profiles.Where(p => p.AutoStart && p.Games.Count > 0).ToArray();
        }

        if (dropped is not null)
            LogArmDropped(dropped);
        Publish(s => s with { Mode = IdleMode(), ArmedProfileId = _armed?.ProfileId });
        return candidates;
    }

    private void Forget(IReadOnlyList<ProcessInfo> running)
    {
        lock (_gate)
        {
            if (_ignored.Count == 0)
                return;
            var alive = running.Select(p => p.ProcessId).ToHashSet();
            _ignored.RemoveWhere(identity => !alive.Contains(identity.ProcessId));
        }
    }

    private (GameMatch Match, GameProcess Game)? FirstNotIgnored(IReadOnlyList<GameMatch> matches)
    {
        foreach (var match in matches)
        {
            if (Earliest(match) is not { } game)
                continue;
            lock (_gate)
            {
                if (!_ignored.Contains(game.Identity))
                    return (match, game);
            }
        }

        return null;
    }

    private GameProcess? Earliest(GameMatch match)
    {
        ProcessIdentity? earliest = null;
        foreach (var processId in match.ProcessIds)
        {
            if (processes.Identify(processId) is { } identity && (earliest is null || identity.StartTime < earliest.Value.StartTime))
                earliest = identity;
        }

        return earliest is { } game ? new GameProcess(match.GameName, game) : null;
    }

    private CoordinatorMode IdleMode() =>
        !_active ? CoordinatorMode.Off
        : _armed is not null ? CoordinatorMode.Armed
        : profiles.Current.Profiles.Any(p => p.AutoStart && p.Games.Count > 0) ? CoordinatorMode.Watching
        : CoordinatorMode.Off;

    private GamingProfile Find(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        return profiles.Current.Find(profileId) ?? throw new ArgumentException($"Unknown profile '{profileId}'", nameof(profileId));
    }

    private async Task WaitAsync(TimeSpan interval, CancellationToken ct)
    {
        using var wake = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_gate)
        {
            if (_wakeRequested)
            {
                _wakeRequested = false;
                return;
            }

            _wakeCts = wake;
        }

        try
        {
            await Task.Delay(interval, time, wake.Token)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ForceYielding);
        }
        finally
        {
            lock (_gate)
            {
                _wakeCts = null;
                _wakeRequested = false;
            }
        }
    }

    private void Wake()
    {
        lock (_gate)
        {
            _wakeRequested = true;
            _wakeCts?.Cancel();
        }
    }

    private void OnProgress(object? sender, SessionProgress progress)
    {
        Publish(s => s.Mode == CoordinatorMode.InSession ? s with { Progress = progress } : s);
    }

    private void Publish(Func<CoordinatorStatus, CoordinatorStatus> change)
    {
        CoordinatorStatus status;
        lock (_gate)
        {
            status = change(_status);
            if (status == _status)
                return;
            _status = status;
        }

        try
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            LogHandlerFailed(ex, status.Mode);
        }
    }

    private sealed record Arming(GamingProfile Profile, IReadOnlySet<string> Confirmed)
    {
        public string ProfileId => Profile.ProfileId;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Session for profile {ProfileId} is armed")]
    private partial void LogArmed(string profileId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Profile {ProfileId}: game already running, irreversible preparation skipped")]
    private partial void LogPreparationSkipped(string profileId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Armed session is cancelled")]
    private partial void LogDisarmed();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Armed profile {ProfileId} is gone or has no games; arm dropped")]
    private partial void LogArmDropped(string profileId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Game {GameName} (PID {ProcessId}) of profile {ProfileId} detected, trigger {Trigger}")]
    private partial void LogAttempt(string profileId, string gameName, int processId, ExecutionTrigger trigger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Game detection step failed")]
    private partial void LogStepFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "StatusChanged handler failed (mode {Mode})")]
    private partial void LogHandlerFailed(Exception exception, CoordinatorMode mode);
}
