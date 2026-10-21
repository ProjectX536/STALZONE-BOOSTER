using Booster.Core.Inventory;

namespace Booster.Core.Abstractions;

/// <summary>Цель замера: ровно одно из <see cref="ProcessName"/> и <see cref="ProcessId"/>.</summary>
public sealed record FrameCaptureRequest(string? ProcessName, int? ProcessId, TimeSpan Duration)
{
    /// <summary>PresentMon завершается вместе с процессом цели (--terminate_on_proc_exit).</summary>
    public bool StopWhenProcessExits { get; init; }

    public FrameCapturePurpose Purpose { get; init; }
}

/// <summary>
/// Назначение замера. У каждого своя ETW-сессия PresentMon: серия бенчмарка, запущенная во время замера игровой сессии,
/// иначе остановила бы его при старте (--stop_existing_session), а отмена одного закрыла бы сессию другого.
/// </summary>
public enum FrameCapturePurpose
{
    Benchmark,
    GameSession,
}

public enum FrameCaptureStatus
{
    Completed,
    NotInstalled,
    ElevationDeclined,
    TargetNotFound,
    NoFrames,
    UnsupportedFormat,
    Failed,
    Cancelled,
}

/// <summary>Как будет запущен захват: напрямую или через запрос UAC.</summary>
public enum FrameCaptureMode
{
    Direct,
    ElevationPrompt,
}

public sealed record FrameCaptureAvailability(bool IsInstalled, string? ExecutablePath, string? Version, FrameCaptureMode Mode);

/// <summary>Кадр из CSV PresentMon. null — колонки нет в CSV или значение NA.</summary>
/// <param name="PresentQpc">TimeInQPC (иначе CPUStartQPC): тики QueryPerformanceCounter, как TimeProvider.System.GetTimestamp().</param>
/// <param name="IntervalMs">Интервал от предыдущего кадра, всегда больше нуля.</param>
/// <param name="CpuBusyMs">Время CPU на кадр; включает ожидание ограничителя FPS.</param>
/// <param name="GpuBusyMs">Время, когда GPU был занят этим кадром.</param>
/// <param name="SyncInterval">0 — без V-Sync, 1 и больше — кадр ждёт обновления экрана.</param>
public readonly record struct FrameSample(long? PresentQpc, double IntervalMs, double? CpuBusyMs, double? GpuBusyMs, int? SyncInterval);

/// <summary>Кадры одного процесса и одной swap chain в порядке показа.</summary>
public sealed record FrameCapture(
    string ProcessName,
    int ProcessId,
    IReadOnlyList<FrameSample> Frames,
    string IntervalColumn,
    int DiscardedRows,
    IReadOnlyList<string> FrameTypes,
    IReadOnlyList<string> PresentModes,
    string CsvPath)
{
    /// <summary>Новый массив интервалов по порядку кадров.</summary>
    public IReadOnlyList<double> IntervalsMs() => Frames.Select(f => f.IntervalMs).ToArray();
}

public sealed record FrameCaptureResult(
    FrameCaptureStatus Status,
    FrameCapture? Capture,
    Reading<AdapterLuid> RenderAdapter,
    string? Detail);

public interface IFrameCaptureProvider
{
    Task<FrameCaptureAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken);

    /// <param name="started">
    /// Вызывается один раз, когда PresentMon запущен (после согласия в UAC); null — не нужен.
    /// Вызов идёт из потока запуска и не должен бросать исключений.
    /// </param>
    Task<FrameCaptureResult> CaptureAsync(FrameCaptureRequest request, Action? started, CancellationToken cancellationToken);
}
