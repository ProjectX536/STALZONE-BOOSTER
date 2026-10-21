using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.Reports;

namespace Booster.Core.SystemHealth;

public static class LatencyAdvisor
{
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
