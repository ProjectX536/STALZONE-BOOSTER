using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Engine;
using Booster.Core.Recovery;
using Booster.Core.Resources;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Profiles;

public enum CoordinatorMode
{
    /// <summary>Опроса нет: координатор не запущен, нет подготовки и профилей с автозапуском.</summary>
    Off,

    /// <summary>Опрос ради профилей с автозапуском.</summary>
    Watching,

    /// <summary>Опрос ради подготовленной сессии.</summary>
    Armed,

    InSession,
}

/// <param name="Plan">План активной сессии.</param>
/// <param name="Progress">Последнее событие активной сессии.</param>
/// <param name="LastSession">Последняя завершённая сессия.</param>
public sealed record CoordinatorStatus(CoordinatorMode Mode, string? ArmedProfileId, SessionPlan? Plan, SessionProgress? Progress,
    SessionRecord? LastSession);

/// <summary>Итог «Подготовить сессию» до игры.</summary>
/// <param name="Preparation">Журнал подготовки; null — выполнять до игры было нечего или игра уже запущена.</param>
/// <param name="SkippedReason">Почему подтверждённое необратимое действие не выполнено (игра уже запущена); null — не пропускалось.</param>
public sealed record PreparationResult(SessionRecord? Preparation, string? SkippedReason);

/// <summary>
/// Обнаружение запуска игры лёгким опросом процессов (только чтение) и запуск сессии: подготовленной пользователем
/// («Подготовить сессию», один раз) или автозапуска профиля. Опрос идёт, только если есть кандидаты.
/// </summary>
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

    // Процессы прошлых попыток: сессия для них уже была (или применять было нечего), повторно не начинается.
    // Процесс забывается, когда его PID пропадает из списка процессов. Игнорируемая игра не заслоняет другие.
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

    /// <summary>Вызывается из фонового потока. Новое состояние — в <see cref="Status"/>.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>Запускает опрос раз в monitoring.gameDetectionIntervalMs; первый шаг — сразу. Повторный вызов ничего не делает.</summary>
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

    /// <summary>Останавливает опрос; активная сессия завершается с восстановлением. Повторный вызов ничего не делает.</summary>
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

        // Токен сессии связан с токеном цикла: отмена завершает Gaming, восстановление менеджер выполняет всегда.
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

    /// <summary>План без игры для экрана подтверждения; только чтение.</summary>
    public Task<SessionPlan> PreviewAsync(string profileId, CancellationToken ct) =>
        manager.PreflightAsync(Find(profileId), game: null, ExecutionTrigger.UserInitiated, NoConfirmations, ct);

    /// <summary>
    /// «Подготовить сессию»: сначала, пока игра не запущена, — подтверждённые необратимые действия (закрытие фоновых
    /// приложений); затем игровая сессия один раз, при следующем запуске игры профиля. Игра уже запущена — необратимое
    /// пропускается, сессия начинается сразу, даже если для этого процесса она уже была.
    /// </summary>
    /// <exception cref="InvalidOperationException">В профиле нет игр, идёт сессия или подготовка.</exception>
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

            // Пока идёт подготовка, опрос сессий не начинает: менеджер занят, а увиденная игра не должна попасть в игнорируемые.
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

    /// <summary>Игра уже запущена: причина, если подтверждённое необратимое действие выполнилось бы до неё; иначе null.</summary>
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

    /// <summary>«Завершить сессию»: Gaming заканчивается, применённое восстанавливается. Сессии нет — ничего.</summary>
    public void StopSession()
    {
        lock (_gate)
        {
            // Асинхронно: продолжение менеджера (восстановление) не выполняется в потоке вызывающего (UI).
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

            // Остановка до начала: иначе менеджер применил бы план и тут же его восстановил.
            if (sessionCts.IsCancellationRequested)
                return;

            // Применять нечего — менеджер пишет об этом в лог и журнал не создаёт; режим сессии не показывается.
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

    /// <summary>Подготовленный профиль, иначе профили с автозапуском и играми.</summary>
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

    /// <summary>Завершившиеся процессы больше не игнорируются: PID и время старта не повторятся.</summary>
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

    /// <summary>Первая по порядку <see cref="ProfileMatcher.MatchAll"/> игра, для процесса которой сессии ещё не было.</summary>
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

    /// <summary>У игры несколько процессов с одним именем — игрой считается запущенный раньше всех.</summary>
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

    /// <summary>Вызывать под <see cref="_gate"/>.</summary>
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
            // ForceYielding: продолжение не выполняется в потоке, который вызвал Wake (UI).
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

    /// <summary>Следующий шаг опроса — сразу, не дожидаясь интервала.</summary>
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

    /// <summary><paramref name="change"/> выполняется под <see cref="_gate"/>; событие — только если состояние изменилось.</summary>
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

    /// <param name="Profile">Профиль в том виде, в каком его план подтвердил пользователь; сохранения после подготовки не учитываются.</param>
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
