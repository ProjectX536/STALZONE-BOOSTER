using System.Globalization;
using System.Text;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Inventory;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Benchmark;

public enum BenchmarkPhase
{
    StartingCapture,
    StartDelay,
    Run,
    Pause,
    Processing,
}

/// <param name="RunIndex">С 0; для StartingCapture и StartDelay — 0.</param>
/// <param name="EndsAt">Конец фазы; null — неизвестен (ожидание UAC, обработка).</param>
public sealed record BenchmarkProgress(BenchmarkPhase Phase, int RunIndex, int RunCount, DateTimeOffset? EndsAt);

/// <summary>
/// Серия прогонов одним захватом PresentMon: окна прогонов отсчитываются от его фактического старта (после UAC),
/// сигналы пишутся только после старта. Система не меняется: дисплеи, схема и источник питания только читаются.
/// </summary>
public sealed partial class BenchmarkRunner(IFrameCaptureProvider capture, BenchmarkRecorder recorder,
    IInventoryDetector<Reading<IReadOnlyList<DisplayInfo>>> displays, IPowerPlanProvider power, IBoosterConfiguration config,
    TimeProvider time, ILogger<BenchmarkRunner> logger)
{
    public const string DisplaySource = "Display detection";
    public const string PowerPlanSource = "PowerGetActiveScheme";
    public const string NotReadNote = "not read: the series did not reach the first run";
    public const string ReadFailedNote = "read failed";
    public const string UnknownSchemeNote = "active power scheme is not in the scheme list";

    /// <summary>Запрещённые в именах файлов Windows символы, кроме управляющих (их проверяет char.IsControl).</summary>
    private const string InvalidFileNameChars = "<>:\"/\\|?*";

    private sealed record Conditions(Reading<IReadOnlyList<DisplayInfo>> Displays, Reading<string> PowerPlan, PowerSource PowerSource);

    private static readonly Conditions NotRead = new(Reading<IReadOnlyList<DisplayInfo>>.Unknown(DisplaySource, NotReadNote),
        Reading<string>.Unknown(PowerPlanSource, NotReadNote), PowerSource.Unknown);

    /// <summary>Отмена — серия со статусом Cancelled, без прогонов; исключение не бросается.</summary>
    /// <exception cref="ArgumentException">Цель — не ровно одно из имени процесса и PID.</exception>
    public async Task<BenchmarkSeries> RunAsync(BenchmarkRequest request, IProgress<BenchmarkProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ProcessName) == request.ProcessId is null)
            throw new ArgumentException("Exactly one of the process name and the process ID is required.", nameof(request));

        var schedule = request.Schedule;
        var thresholds = config.Current.Thresholds;
        var startedAt = time.GetLocalNow();
        var id = SeriesId(startedAt, request);

        void Report(BenchmarkPhase phase, int runIndex, DateTimeOffset? endsAt) =>
            progress?.Report(new BenchmarkProgress(phase, runIndex, schedule.RunCount, endsAt));

        BenchmarkSeries Build(FrameCaptureResult result, IReadOnlyList<RunWindow> windows, IReadOnlyList<BenchmarkSample> samples,
            Conditions conditions) =>
            BenchmarkSeriesBuilder.Build(new BenchmarkCaptureData(id, request, startedAt, result, windows, samples, conditions.Displays,
                conditions.PowerPlan, conditions.PowerSource), thresholds);

        Report(BenchmarkPhase.StartingCapture, 0, null);
        var started = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var captureRequest = new FrameCaptureRequest(request.ProcessName, request.ProcessId, schedule.CaptureDuration);
        var captureTask = Task.Run(() => CaptureAsync(captureRequest, () => started.TrySetResult(time.GetTimestamp()), ct), CancellationToken.None);

        await Task.WhenAny(started.Task, captureTask).ConfigureAwait(false);
        if (!started.Task.IsCompleted)
        {
            // Отказ UAC, нет PresentMon, ошибка до старта: расписание не начиналось, сигналы не писались.
            return Build(await captureTask.ConfigureAwait(false), [], [], NotRead);
        }

        var t0 = await started.Task.ConfigureAwait(false);
        var windows = schedule.Windows(t0, time.TimestampFrequency);
        using var stopRecording = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var recording = RecordAsync(request, stopRecording.Token);
        // Запущенный PresentMon может завершиться раньше расписания (ошибка ETW, сбой): отсчитывать прогоны дальше незачем.
        using var scheduleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var captureEnded = captureTask.ContinueWith(static (_, state) => ((CancellationTokenSource)state!).Cancel(), scheduleCts,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        var scheduleToken = scheduleCts.Token;
        Task<Conditions>? conditions = null;
        var cancelled = false;
        try
        {
            Report(BenchmarkPhase.StartDelay, 0, EndsAt(t0, schedule.StartDelay));
            await DelayUntilAsync(t0, schedule.StartDelay, scheduleToken).ConfigureAwait(false);
            for (var run = 0; run < schedule.RunCount; run++)
            {
                var runEnd = schedule.RunStartOffset(run) + schedule.RunDuration;
                Report(BenchmarkPhase.Run, run, EndsAt(t0, runEnd));
                if (run == 0)
                    conditions = Task.Run(() => ReadConditionsAsync(ct), CancellationToken.None);
                await DelayUntilAsync(t0, runEnd, scheduleToken).ConfigureAwait(false);
                if (run + 1 < schedule.RunCount)
                {
                    var nextStart = schedule.RunStartOffset(run + 1);
                    Report(BenchmarkPhase.Pause, run, EndsAt(t0, nextStart));
                    await DelayUntilAsync(t0, nextStart, scheduleToken).ConfigureAwait(false);
                }
            }

            Report(BenchmarkPhase.Processing, schedule.RunCount - 1, null);
        }
        catch (OperationCanceledException) when (scheduleToken.IsCancellationRequested)
        {
            // Иначе захват закончился раньше расписания: его статус и причина идут в серию как есть.
            cancelled = ct.IsCancellationRequested;
        }

        // При отмене провайдер сам вернёт Cancelled; повышенный PresentMon дорабатывает свой --timed.
        var result = await captureTask.ConfigureAwait(false);
        await captureEnded.ConfigureAwait(false);
        await stopRecording.CancelAsync().ConfigureAwait(false);
        var samples = await recording.ConfigureAwait(false);
        var read = conditions is null ? NotRead : await conditions.ConfigureAwait(false);
        if (cancelled)
            result = new FrameCaptureResult(FrameCaptureStatus.Cancelled, null, result.RenderAdapter, null);
        return Build(result, windows, samples, read);
    }

    /// <summary><c>yyyyMMdd-HHmmss-игра</c>: имя без .exe (или pid), запрещённые в именах файлов символы заменены на «_».</summary>
    private static string SeriesId(DateTimeOffset startedAt, BenchmarkRequest request)
    {
        var name = request.ProcessName is { } processName
            ? processName.Trim() is var trimmed && trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? trimmed[..^4] : trimmed
            : "pid" + request.ProcessId!.Value.ToString(CultureInfo.InvariantCulture);
        var safe = new StringBuilder(name.Length);
        foreach (var c in name)
            safe.Append(char.IsControl(c) || InvalidFileNameChars.Contains(c) ? '_' : c);
        return startedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + safe;
    }

    private DateTimeOffset EndsAt(long t0, TimeSpan offset) => time.GetLocalNow() + (offset - time.GetElapsedTime(t0));

    private async Task DelayUntilAsync(long t0, TimeSpan offset, CancellationToken ct)
    {
        var wait = offset - time.GetElapsedTime(t0);
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, time, ct).ConfigureAwait(false);
        else
            ct.ThrowIfCancellationRequested();
    }

    private async Task<FrameCaptureResult> CaptureAsync(FrameCaptureRequest request, Action started, CancellationToken ct)
    {
        try
        {
            return await capture.CaptureAsync(request, started, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new FrameCaptureResult(FrameCaptureStatus.Cancelled, null, Reading<AdapterLuid>.Unknown(), null);
        }
        catch (Exception ex)
        {
            LogCaptureFailed(ex);
            return new FrameCaptureResult(FrameCaptureStatus.Failed, null, Reading<AdapterLuid>.Unknown(), ex.Message);
        }
    }

    private async Task<IReadOnlyList<BenchmarkSample>> RecordAsync(BenchmarkRequest request, CancellationToken stop)
    {
        try
        {
            return await recorder.RecordAsync(request.ProcessName, request.ProcessId, stop).ConfigureAwait(false);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && stop.IsCancellationRequested))
        {
            LogRecordingFailed(ex);
            return [];
        }
    }

    /// <summary>Только чтение, в пуле потоков. Ошибка — Failed/Unknown в условиях, а не сбой серии.</summary>
    private async Task<Conditions> ReadConditionsAsync(CancellationToken ct)
    {
        Reading<IReadOnlyList<DisplayInfo>> displayReading;
        try
        {
            displayReading = await displays.DetectAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            displayReading = NotRead.Displays;
        }
        catch (Exception ex)
        {
            LogConditionFailed(ex, "displays");
            displayReading = Reading<IReadOnlyList<DisplayInfo>>.Failure(DisplaySource, ReadFailedNote);
        }

        Reading<string> plan;
        try
        {
            var active = power.GetActiveScheme();
            plan = power.ListSchemes().FirstOrDefault(scheme => scheme.Id == active) is { } scheme
                ? Reading<string>.Known(scheme.Name, PowerPlanSource)
                : Reading<string>.Unknown(PowerPlanSource, UnknownSchemeNote);
        }
        catch (Exception ex)
        {
            LogConditionFailed(ex, "power plan");
            plan = Reading<string>.Failure(PowerPlanSource, ReadFailedNote);
        }

        PowerSource source;
        try
        {
            source = power.GetPowerSource();
        }
        catch (Exception ex)
        {
            LogConditionFailed(ex, "power source");
            source = PowerSource.Unknown;
        }

        return new Conditions(displayReading, plan, source);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Benchmark capture failed")]
    private partial void LogCaptureFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Benchmark signal recording failed; the series has no telemetry")]
    private partial void LogRecordingFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Benchmark conditions: {Part} read failed")]
    private partial void LogConditionFailed(Exception ex, string part);
}
