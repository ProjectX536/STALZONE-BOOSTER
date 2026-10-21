using Booster.Core.Configuration;

namespace Booster.Core.Benchmark;

/// <param name="StutterTimePercent">Сумма интервалов-рывков от длительности прогона, %.</param>
/// <param name="WorstStutterMs">Самый долгий интервал-рывок; 0 — рывков нет.</param>
/// <param name="MeanFrameToFrameDeltaMs">Среднее |Δᵢ − Δᵢ₋₁|: мера неровности даже без рывков.</param>
public sealed record StutterResult(int StutterCount, double StuttersPerMinute, double StutterTimePercent, double WorstStutterMs,
    double MeanFrameToFrameDeltaMs);

/// <summary>
/// Интервал Δᵢ — рывок, если Δᵢ ≥ factor × медиана соседей и Δᵢ − медиана ≥ minimumExcessMs.
/// Соседи — windowFrames интервалов вокруг Δᵢ, поровну до и после, без него самого; у краёв прогона окно сдвигается внутрь,
/// в прогоне короче окна берутся все остальные интервалы. Медиана соседей, а не всего прогона, — чтобы смена сцены
/// не давала ложных рывков; соседи с обеих сторон — чтобы резкий переход FPS не давал серию рывков.
/// </summary>
public static class StutterAnalyzer
{
    private static readonly StutterResult None = new(0, 0, 0, 0, 0);

    /// <summary>Определение — docs/ARCHITECTURE.md, раздел 10. Пустой вход — нули.</summary>
    public static StutterResult Analyze(IReadOnlyList<double> intervalsMs, StutterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(intervalsMs);
        ArgumentNullException.ThrowIfNull(settings);

        var n = intervalsMs.Count;
        if (n == 0)
            return None;

        var neighbours = Math.Min(settings.WindowFrames, n - 1);
        var buffer = new double[neighbours];
        var count = 0;
        var stutterMs = 0.0;
        var worst = 0.0;
        for (var i = 0; neighbours > 0 && i < n; i++)
        {
            // Окно из neighbours + 1 позиций с Δᵢ посередине, прижатое к краям прогона.
            var start = Math.Clamp(i - neighbours / 2, 0, n - neighbours - 1);
            var filled = 0;
            for (var j = start; j <= start + neighbours; j++)
            {
                if (j != i)
                    buffer[filled++] = intervalsMs[j];
            }

            var median = Median.Of(buffer);
            var interval = intervalsMs[i];
            if (interval >= settings.Factor * median && interval - median >= settings.MinimumExcessMs)
            {
                count++;
                stutterMs += interval;
                worst = Math.Max(worst, interval);
            }
        }

        var totalMs = intervalsMs.Sum();
        var deltaSum = 0.0;
        for (var i = 1; i < n; i++)
            deltaSum += Math.Abs(intervalsMs[i] - intervalsMs[i - 1]);

        return new StutterResult(
            count,
            count / (totalMs / 60_000),
            stutterMs / totalMs * 100,
            worst,
            n > 1 ? deltaSum / (n - 1) : 0);
    }
}
