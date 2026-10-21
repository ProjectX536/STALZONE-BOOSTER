using Booster.Core.Abstractions;
using Booster.Core.Resources;
using Booster.Core.Sensors;

namespace Booster.Platform.Windows.Monitoring;

public static class CpuTelemetryCalculator
{
    private const double NominalPercent = 100;

    public static CpuTelemetry Compute(double? performancePercent, double? nominalMhz, double? limitPercent, string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var nominal = nominalMhz is > 0 ? nominalMhz : null;
        var effective = performancePercent is { } performance && nominal is { } mhz
            ? Reading<double>.Known(mhz * performance / NominalPercent, source)
            : Reading<double>.Unknown(source);
        var limited = limitPercent is { } guaranteed
            ? Reading<double>.Known(Math.Max(0, NominalPercent - guaranteed), source)
            : Reading<double>.Unknown(source);
        return new(effective, Value(nominal, source), Value(performancePercent, source), limited,
            Reading<double>.Unsupported(source, ReportStrings.CpuTemperatureNoSource));
    }

    private static Reading<double> Value(double? value, string source) =>
        value is { } v ? Reading<double>.Known(v, source) : Reading<double>.Unknown(source);
}
