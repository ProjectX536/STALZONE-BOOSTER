using Booster.Core.Abstractions;

namespace Booster.Core.Benchmark;

/// <summary>Медианы по кадрам. null — нужной колонки нет ни у одного кадра.</summary>
/// <param name="GpuBusyShare">Медиана GpuBusy / интервал, не больше 1.</param>
/// <param name="CpuBusyShare">Медиана CpuBusy / интервал, не больше 1. CpuBusy включает ожидание ограничителя FPS.</param>
/// <param name="SyncedShare">Доля кадров с SyncInterval ≥ 1 среди кадров, где он известен.</param>
/// <param name="TypicalSyncInterval">Самое частое значение SyncInterval; при равенстве — меньшее.</param>
public sealed record FrameStats(int Frames, double? GpuBusyShare, double? CpuBusyShare, double? SyncedShare, int? TypicalSyncInterval);

public static class FrameStatsCalculator
{
    public static FrameStats Compute(IReadOnlyList<FrameSample> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);

        var syncIntervals = frames.Where(f => f.SyncInterval is not null).Select(f => f.SyncInterval!.Value).ToArray();
        return new FrameStats(
            frames.Count,
            BusyShare(frames, f => f.GpuBusyMs),
            BusyShare(frames, f => f.CpuBusyMs),
            syncIntervals.Length == 0 ? null : (double)syncIntervals.Count(s => s >= 1) / syncIntervals.Length,
            syncIntervals.Length == 0
                ? null
                : syncIntervals.GroupBy(s => s).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key);
    }

    private static double? BusyShare(IReadOnlyList<FrameSample> frames, Func<FrameSample, double?> busyMs)
    {
        var shares = frames
            .Where(f => busyMs(f) is not null && f.IntervalMs > 0)
            .Select(f => Math.Min(busyMs(f)!.Value / f.IntervalMs, 1.0))
            .ToArray();
        return shares.Length == 0 ? null : Median.Of(shares);
    }
}
