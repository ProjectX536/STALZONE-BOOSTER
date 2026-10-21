namespace Booster.Core.Metrics;

/// <summary>
/// Расчёт метрик по интервалам между кадрами (мс):
/// средний FPS = 1000·N / Σинтервалов; перцентили — по ближайшему рангу ⌈p·N/100⌉;
/// 1% low = 1000 / среднее k = ⌈N/100⌉ самых долгих интервалов.
/// Интервалы ≤ 0, NaN и бесконечные отбрасываются и считаются в <see cref="FrameMetricsResult.InvalidIntervals"/>.
/// </summary>
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
        // p·N считается в целых, чтобы 0,99·300 не превратилось в 297,000…1 и ранг не сдвинулся
        var rank = (int)Math.Ceiling(percent * (long)sorted.Length / 100.0);
        return sorted[Math.Max(rank, 1) - 1];
    }
}
