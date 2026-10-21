using Booster.Core.Abstractions;
using Booster.Core.Configuration;

namespace Booster.Core.Sensors;

public static class ThermalThrottleDetector
{
    private const GpuThrottleReasons OtherReasons =
        GpuThrottleReasons.PowerSource | GpuThrottleReasons.ApiTriggered | GpuThrottleReasons.InsufficientPower | GpuThrottleReasons.Other;

    public static ThrottleAssessment Assess(IReadOnlyList<SensorSnapshot> samples, ThrottleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(settings);
        var enough = samples.Count >= settings.MinimumSamples;
        var findings = new List<ThrottleFinding>();
        foreach (var series in GroupByAdapter(samples))
            AssessAdapter(series, enough, settings, findings);
        AssessCpu(samples, findings);
        return new(samples.Count, settings.MinimumSamples, enough, findings);
    }

    private static void AssessAdapter(AdapterSeries series, bool enough, ThrottleSettings settings, List<ThrottleFinding> findings)
    {
        var all = series.Samples;
        AddIfAny(findings, series, ThrottleSignal.GpuDriverThermal, ThrottleConfidence.High,
            [.. all.Where(t => HasReason(t, GpuThrottleReasons.Thermal))], t => t.TemperatureC, highest: true);
        AddIfAny(findings, series, ThrottleSignal.GpuDriverPowerLimit, ThrottleConfidence.High,
            [.. all.Where(t => HasReason(t, GpuThrottleReasons.PowerLimit))], t => t.CoreClockMhz, highest: false);
        AddIfAny(findings, series, ThrottleSignal.GpuDriverOther, ThrottleConfidence.High,
            [.. all.Where(t => HasReason(t, OtherReasons))], t => t.CoreClockMhz, highest: false);
        AddIfAny(findings, series, ThrottleSignal.GpuTemperatureAtLimit, ThrottleConfidence.Medium,
            [.. all.Where(t => AtTemperatureLimit(t, settings))], t => t.TemperatureC, highest: true);

        if (!enough)
            return;
        var busy = all
            .Where(t => t.UtilizationPercent.IsAvailable && t.UtilizationPercent.Value >= settings.GpuBusyUtilizationPercent && t.CoreClockMhz.IsAvailable)
            .ToList();
        if (busy.Count < 2)
            return;
        var floor = busy.Max(t => t.CoreClockMhz.Value) * (1 - (settings.ClockDropPercent / 100));
        var dropped = busy.Where(t => t.CoreClockMhz.Value < floor).ToList();
        var explained = dropped.Any(t =>
            HasReason(t, GpuThrottleReasons.Thermal | GpuThrottleReasons.PowerLimit) || AtTemperatureLimit(t, settings));
        AddIfAny(findings, series, ThrottleSignal.GpuClockDropUnderLoad, explained ? ThrottleConfidence.Medium : ThrottleConfidence.Low,
            dropped, t => t.CoreClockMhz, highest: false);
    }

    private static void AssessCpu(IReadOnlyList<SensorSnapshot> samples, List<ThrottleFinding> findings)
    {
        var limited = samples
            .Select(s => s.Cpu.PerformanceLimitPercent)
            .Where(r => r.IsAvailable && r.Value > 0)
            .ToList();
        if (limited.Count > 0)
            findings.Add(new(ThrottleSignal.CpuPerformanceLimited, ThrottleConfidence.Low, null, limited.Count, samples.Count, Extreme(limited, highest: true)));
    }

    private static void AddIfAny(List<ThrottleFinding> findings, AdapterSeries series, ThrottleSignal signal, ThrottleConfidence confidence,
        IReadOnlyList<GpuTelemetry> matched, Func<GpuTelemetry, Reading<double>> peakOf, bool highest)
    {
        if (matched.Count > 0)
            findings.Add(new(signal, confidence, series.Name, matched.Count, series.Samples.Count, Extreme(matched.Select(peakOf), highest)));
    }

    private static bool HasReason(GpuTelemetry telemetry, GpuThrottleReasons reasons) =>
        telemetry.ThrottleReasons.IsAvailable && (telemetry.ThrottleReasons.Value & reasons) != 0;

    private static bool AtTemperatureLimit(GpuTelemetry telemetry, ThrottleSettings settings) =>
        telemetry.TemperatureC.IsAvailable
        && telemetry.TemperatureLimitC.IsAvailable
        && telemetry.TemperatureC.Value >= telemetry.TemperatureLimitC.Value - settings.TemperatureMarginC;

    private static Reading<double> Extreme(IEnumerable<Reading<double>> readings, bool highest)
    {
        Reading<double>? best = null;
        foreach (var reading in readings.Where(r => r.IsAvailable))
        {
            if (best is not { } current || (highest ? reading.Value > current.Value : reading.Value < current.Value))
                best = reading;
        }
        return best ?? Reading<double>.Unknown();
    }

    private static List<AdapterSeries> GroupByAdapter(IReadOnlyList<SensorSnapshot> samples)
    {
        var byKey = new Dictionary<string, AdapterSeries>(StringComparer.Ordinal);
        var ordered = new List<AdapterSeries>();
        foreach (var gpu in samples.SelectMany(s => s.Gpus))
        {
            var key = gpu.Adapter.Luid.IsAvailable ? gpu.Adapter.Luid.Value.ToString() : "name:" + gpu.Adapter.Name;
            if (!byKey.TryGetValue(key, out var series))
            {
                series = new AdapterSeries(gpu.Adapter.Name, []);
                byKey.Add(key, series);
                ordered.Add(series);
            }
            series.Samples.Add(gpu.Telemetry);
        }
        return ordered;
    }

    private sealed record AdapterSeries(string Name, List<GpuTelemetry> Samples);
}
