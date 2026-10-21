namespace Booster.Core.Metrics;

public static class FrameMetricsCalculator
{
    public static FrameMetricsResult Calculate(IReadOnlyList<double> frameIntervalsMs, int minimumFrames)
    {
        ArgumentNullException.ThrowIfNull(frameIntervalsMs);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumFrames, 1);

        var sorted = frameIntervalsMs.Where(v => double.IsFinite(v) && v > 0).ToArray();
        var invalid = frameIntervalsMs.Count - sorted.Length;
        var n = sorted.Length;
        if (n < minimumFrames)
            return new(FrameMetricsStatus.InsufficientData, null, n, invalid);

        Array.Sort(sorted);
        var duration = sorted.Sum();
        var tail = (int)Math.Ceiling(n / 100.0);
        var tailMean = sorted.AsSpan(n - tail).ToArray().Average();

        var metrics = new FrameMetrics(
            FrameCount: n,
            DurationMs: duration,
            AverageFps: 1000.0 * n / duration,
            OnePercentLowFps: 1000.0 / tailMean,
            FrameTimeP50Ms: NearestRank(sorted, 50),
            FrameTimeP95Ms: NearestRank(sorted, 95),
            FrameTimeP99Ms: NearestRank(sorted, 99));
        return new(FrameMetricsStatus.Ok, metrics, n, invalid);
    }

    private static double NearestRank(double[] sorted, int percent)
    {
        // В целых, чтобы 0,99·300 не стало 297,000…1.
        var rank = (int)Math.Ceiling(percent * (long)sorted.Length / 100.0);
        return sorted[Math.Max(rank, 1) - 1];
    }
}
