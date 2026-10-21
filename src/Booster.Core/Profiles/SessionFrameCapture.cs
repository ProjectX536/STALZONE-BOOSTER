using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.Metrics;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Profiles;

public enum SessionFrameStatus
{
    /// <summary>Профиль не собирает метрики кадров: выключены monitoring.collectFrameMetrics или monitoring.enabled.</summary>
    Disabled,

    /// <summary>PresentMon без запроса UAC не запустить, а во время игры Booster этот запрос не показывает.</summary>
    NeedsElevation,

    NotInstalled,
    Capturing,
    Completed,

    /// <summary>Замер не удался; причина — в <see cref="SessionFrameSummary.Detail"/> и в логе.</summary>
    Failed,
}

/// <summary>Метрики кадров первых benchmark.sessionCaptureSeconds сессии.</summary>
/// <param name="Duration">Запланированная длительность замера. Фактическая — в метриках: меньше, если игра закрылась раньше.</param>
/// <param name="Metrics">Только при <see cref="SessionFrameStatus.Completed"/>; InsufficientData — кадров слишком мало.</param>
/// <param name="Stutter">Рывки; только если метрики посчитаны.</param>
public sealed record SessionFrameSummary(SessionFrameStatus Status, TimeSpan Duration, FrameMetricsResult? Metrics, StutterResult? Stutter,
    string? Detail);

/// <summary>
/// Метрики кадров начала игровой сессии (collectFrameMetrics): PresentMon по PID игры в фоне, первые
/// benchmark.sessionCaptureSeconds. Запрос UAC во время игры не показывается — без прав замер не начинается.
/// Сбой замера не затрагивает ни сессию, ни восстановление: состояние становится <see cref="SessionFrameStatus.Failed"/>.
/// </summary>
public sealed partial class SessionFrameCapture(IFrameCaptureProvider capture, IBoosterConfiguration config,
    ILogger<SessionFrameCapture> logger)
{
    /// <summary>Начинает замер в фоне. Конец — отмена <paramref name="ct"/> или <see cref="SessionFrameRun.DisposeAsync"/>.</summary>
    public SessionFrameRun Start(ProfileMonitoring monitoring, GameProcess game, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(monitoring);
        ArgumentNullException.ThrowIfNull(game);
        var duration = TimeSpan.FromSeconds(config.Current.Settings.Benchmark.SessionCaptureSeconds);
        if (monitoring is not { Enabled: true, CollectFrameMetrics: true })
            return new SessionFrameRun(null, logger) { Summary = Summary(SessionFrameStatus.Disabled, duration) };

        var run = new SessionFrameRun(CancellationTokenSource.CreateLinkedTokenSource(ct), logger);
        run.Track(Task.Run(() => CaptureAsync(game, duration, run), CancellationToken.None));
        return run;
    }

    private async Task CaptureAsync(GameProcess game, TimeSpan duration, SessionFrameRun run)
    {
        var ct = run.Token;
        try
        {
            var availability = await capture.CheckAvailabilityAsync(ct).ConfigureAwait(false);
            if (!availability.IsInstalled || availability.Mode != FrameCaptureMode.Direct)
            {
                var status = availability.IsInstalled ? SessionFrameStatus.NeedsElevation : SessionFrameStatus.NotInstalled;
                LogSkipped(game.Name, status);
                run.Summary = Summary(status, duration);
                return;
            }

            var request = new FrameCaptureRequest(null, game.Identity.ProcessId, duration)
            {
                StopWhenProcessExits = true,
                Purpose = FrameCapturePurpose.GameSession,
            };
            run.Summary = Summary(SessionFrameStatus.Capturing, duration);
            LogStarted(game.Name, game.Identity.ProcessId, (long)duration.TotalSeconds);
            var result = await capture.CaptureAsync(request, null, ct).ConfigureAwait(false);

            // Сессия закончилась раньше замера: показывать уже нечего.
            if (ct.IsCancellationRequested)
                return;

            var summary = Summarize(result, duration, config.Current.Thresholds);
            if (summary.Status == SessionFrameStatus.Failed)
                LogCaptureFailed(game.Name, summary.Detail);
            run.Summary = summary;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LogCaptureThrew(ex, game.Name);
            run.Summary = Summary(SessionFrameStatus.Failed, duration) with { Detail = $"{ex.GetType().Name}: {ex.Message}" };
        }
    }

    private static SessionFrameSummary Summarize(FrameCaptureResult result, TimeSpan duration, Thresholds thresholds)
    {
        if (result is { Status: FrameCaptureStatus.Completed, Capture: { } frames })
        {
            var intervals = frames.IntervalsMs();
            var metrics = FrameMetricsCalculator.Calculate(intervals, thresholds.MinimumFramesForMetrics);
            var stutter = metrics.Status == FrameMetricsStatus.Ok ? StutterAnalyzer.Analyze(intervals, thresholds.Stutter) : null;
            return new SessionFrameSummary(SessionFrameStatus.Completed, duration, metrics, stutter, null);
        }

        var status = result.Status switch
        {
            FrameCaptureStatus.NotInstalled => SessionFrameStatus.NotInstalled,
            FrameCaptureStatus.ElevationDeclined => SessionFrameStatus.NeedsElevation,
            _ => SessionFrameStatus.Failed,
        };
        return Summary(status, duration) with { Detail = $"{result.Status}: {result.Detail}" };
    }

    private static SessionFrameSummary Summary(SessionFrameStatus status, TimeSpan duration) => new(status, duration, null, null, null);

    [LoggerMessage(Level = LogLevel.Information, Message = "Frame capture of game {Game} (PID {ProcessId}) started for {Seconds} s")]
    private partial void LogStarted(string game, int processId, long seconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Frame capture of game {Game} skipped: {Status}")]
    private partial void LogSkipped(string game, SessionFrameStatus status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Frame capture of game {Game} failed: {Detail}")]
    private partial void LogCaptureFailed(string game, string? detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Frame capture of game {Game} failed")]
    private partial void LogCaptureThrew(Exception exception, string game);
}

/// <summary>Замер кадров одной сессии. <see cref="DisposeAsync"/> останавливает его, не дожидаясь PresentMon.</summary>
public sealed partial class SessionFrameRun : IAsyncDisposable
{
    private readonly CancellationTokenSource? _cts;
    private readonly ILogger _logger;
    private SessionFrameSummary? _summary;
    private Task _task = Task.CompletedTask;
    private int _disposed;

    /// <param name="cts">null — замер не запускается.</param>
    internal SessionFrameRun(CancellationTokenSource? cts, ILogger logger)
    {
        _cts = cts;
        _logger = logger;
    }

    /// <summary>Состояние замера; null — пока проверяется PresentMon.</summary>
    public SessionFrameSummary? Summary
    {
        get => Volatile.Read(ref _summary);
        internal set => Volatile.Write(ref _summary, value);
    }

    internal CancellationToken Token => _cts?.Token ?? CancellationToken.None;

    internal void Track(Task task) => _task = task;

    /// <summary>Не бросает исключений: остановка замера не должна помешать восстановлению.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_cts is not { } cts || Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogStopFailed(_logger, ex);
        }

        // Источник отмены освобождается, когда фоновый замер перестаёт им пользоваться.
        _ = _task.ContinueWith(_ => cts.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopping the session frame capture failed")]
    private static partial void LogStopFailed(ILogger logger, Exception exception);
}
