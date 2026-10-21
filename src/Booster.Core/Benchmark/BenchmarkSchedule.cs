using Booster.Core.Abstractions;
using Booster.Core.Configuration;

namespace Booster.Core.Benchmark;

/// <summary>
/// Серия в одной записи PresentMon: задержка старта, затем прогоны с паузами между ними (docs/ARCHITECTURE.md, раздел 10).
/// Смещения отсчитываются от запуска PresentMon.
/// </summary>
public sealed record BenchmarkSchedule(int RunCount, TimeSpan RunDuration, TimeSpan Pause, TimeSpan StartDelay)
{
    /// <summary>Запас на остановку ETW-сессии, чтобы конец записи не обрезал последний прогон.</summary>
    private static readonly TimeSpan StopMargin = TimeSpan.FromSeconds(2);

    /// <summary>Задержка + прогоны + паузы между ними; плюс 2 с запаса на остановку ETW — для --timed.</summary>
    public TimeSpan CaptureDuration => StartDelay + RunDuration * RunCount + Pause * (RunCount - 1) + StopMargin;

    /// <summary>Начало прогона от запуска PresentMon; <paramref name="index"/> с 0.</summary>
    public TimeSpan RunStartOffset(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, RunCount);
        return StartDelay + (RunDuration + Pause) * index;
    }

    /// <param name="startTimestamp">Метка QPC запуска PresentMon.</param>
    /// <param name="timestampFrequency">Тиков QPC в секунду (TimeProvider.TimestampFrequency).</param>
    public IReadOnlyList<RunWindow> Windows(long startTimestamp, long timestampFrequency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timestampFrequency);

        var windows = new RunWindow[RunCount];
        for (var i = 0; i < RunCount; i++)
        {
            var start = startTimestamp + Ticks(RunStartOffset(i), timestampFrequency);
            windows[i] = new RunWindow(i, start, start + Ticks(RunDuration, timestampFrequency));
        }
        return windows;
    }

    public static BenchmarkSchedule From(BenchmarkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new BenchmarkSchedule(settings.RunCount, TimeSpan.FromSeconds(settings.RunSeconds),
            TimeSpan.FromSeconds(settings.PauseSeconds), TimeSpan.FromSeconds(settings.StartDelaySeconds));
    }

    // Int128: тики TimeSpan × частота QPC переполняют long уже на часовых интервалах при частоте в ГГц.
    private static long Ticks(TimeSpan span, long frequency) => (long)((Int128)span.Ticks * frequency / TimeSpan.TicksPerSecond);
}

/// <summary>Окно прогона в тиках QPC: [StartQpc, EndQpc).</summary>
public readonly record struct RunWindow(int Index, long StartQpc, long EndQpc)
{
    public bool Contains(long timestamp) => timestamp >= StartQpc && timestamp < EndQpc;
}

public static class RunSlicer
{
    /// <summary>Кадры с меткой в окне, кроме первого: его интервал начался до окна. Кадры без метки не попадают никуда.</summary>
    public static IReadOnlyList<IReadOnlyList<FrameSample>> Slice(IReadOnlyList<FrameSample> frames, IReadOnlyList<RunWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(windows);

        return windows
            .Select(window => (IReadOnlyList<FrameSample>)frames
                .Where(f => f.PresentQpc is { } timestamp && window.Contains(timestamp))
                .Skip(1)
                .ToArray())
            .ToArray();
    }
}
