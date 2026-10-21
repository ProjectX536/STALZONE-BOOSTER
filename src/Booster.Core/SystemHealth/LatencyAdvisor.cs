using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.Reports;

namespace Booster.Core.SystemHealth;

/// <summary>
/// Рекомендации по времени DPC и прерываний в серии бенчмарка. Какой драйвер виноват, показывает только трасса WPR, а драйверы
/// Booster не меняет — поэтому рекомендация только ручная. Нет серии или значения — нет и рекомендации.
/// </summary>
public static class LatencyAdvisor
{
    /// <summary>Пик DPC или прерываний не ниже порога → Warning, L3, аргумент — пик в инвариантной культуре.</summary>
    /// <param name="telemetry">Сводка серии бенчмарка текущей сессии; null — серии нет.</param>
    public static IReadOnlyList<SystemAdvice> Advise(SeriesTelemetry? telemetry, BottleneckSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (telemetry is null)
            return [];

        var advice = new List<SystemAdvice>();
        if (AtLeast(telemetry.DpcTimePeakPercent, settings.DpcTimePercent))
            advice.Add(Warning(AdviceId.DpcTimeHigh, telemetry.DpcTimePeakPercent.Value));
        if (AtLeast(telemetry.InterruptTimePeakPercent, settings.InterruptTimePercent))
            advice.Add(Warning(AdviceId.InterruptTimeHigh, telemetry.InterruptTimePeakPercent.Value));
        return advice;
    }

    private static bool AtLeast(Reading<double> reading, double threshold) => reading.IsAvailable && reading.Value >= threshold;

    private static SystemAdvice Warning(AdviceId id, double peakPercent) => new(id, AdviceModule.Latency, FindingSeverity.Warning,
        SafetyLevel.L3ManualOnly, [peakPercent.ToString("0.#", CultureInfo.InvariantCulture)]);
}
