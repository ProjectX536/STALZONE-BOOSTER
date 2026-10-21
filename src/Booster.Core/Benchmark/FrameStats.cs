using Booster.Core.Abstractions;

namespace Booster.Core.Benchmark;

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
