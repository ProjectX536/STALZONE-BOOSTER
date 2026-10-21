using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Resources;
using Booster.Core.Sensors;

namespace Booster.Core.Reports;

/// <summary>
/// Поля датчиков одного адаптера и CPU — общие для отчёта и живого экрана «Оборудование», чтобы тексты значений
/// и статусов («Неизвестно», «Нужны права администратора») совпадали.
/// </summary>
public static class SensorFieldFormatter
{
    public static IReadOnlyList<(string Label, ReportCell Value)> Gpu(GpuSensorReading reading, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(culture);
        var telemetry = reading.Telemetry;
        return
        [
            (ReportStrings.SensorUtilization, Value(telemetry.UtilizationPercent, v => Percent(v, culture))),
            (ReportStrings.SensorTemperature, Temperature(telemetry.TemperatureC, telemetry.TemperatureLimitC, culture)),
            (ReportStrings.SensorCoreClock, Value(telemetry.CoreClockMhz, v => Mhz(v, culture))),
            (ReportStrings.SensorMemoryClock, Value(telemetry.MemoryClockMhz, v => Mhz(v, culture))),
            (ReportStrings.SensorVram, Vram(telemetry.VramUsedBytes, telemetry.VramTotalBytes, culture)),
            (ReportStrings.SensorFan, Value(telemetry.FanRpm, rpm => Format(culture, ReportStrings.UnitRpm, rpm.ToString(culture)))),
            (ReportStrings.SensorPower, Value(telemetry.PowerWatts, watts => Format(culture, ReportStrings.UnitWatts, watts.ToString("0", culture)))),
            (ReportStrings.SensorThrottleReasons, Value(telemetry.ThrottleReasons, Reasons)),
            (ReportStrings.SensorProvider, new ReportCell(reading.ProviderId)),
        ];
    }

    public static IReadOnlyList<(string Label, ReportCell Value)> Cpu(CpuTelemetry cpu, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(cpu);
        ArgumentNullException.ThrowIfNull(culture);
        return
        [
            (ReportStrings.CpuEffectiveFrequency, Value(cpu.EffectiveFrequencyMhz, v => Mhz(v, culture))),
            (ReportStrings.CpuNominalFrequency, Value(cpu.NominalFrequencyMhz, v => Mhz(v, culture))),
            (ReportStrings.CpuPerformanceLimit, Value(cpu.PerformanceLimitPercent, v => Percent(v, culture))),
            (ReportStrings.CpuSensorTemperature, Value(cpu.TemperatureC, v => Celsius(v, culture))),
        ];
    }

    /// <summary>Значение или статус, подсказка — источник и пояснение.</summary>
    public static ReportCell Value<T>(Reading<T> reading, Func<T, string> format) => new(
        ReadingFormatter.Format(reading, format),
        ReadingFormatter.SourceText(reading.Source, reading.Note),
        reading.IsAvailable ? CellTone.Normal : CellTone.Unavailable);

    public static string Mhz(double value, CultureInfo culture) => Format(culture, ReportStrings.UnitMhz, value.ToString("0", culture));

    public static string Percent(double value, CultureInfo culture) => Format(culture, ReportStrings.UnitPercent, value.ToString("0", culture));

    /// <summary>Проценты с сотыми: время DPC и прерываний обычно меньше процента, целое число показало бы 0.</summary>
    public static string FractionPercent(double value, CultureInfo culture) =>
        Format(culture, ReportStrings.UnitPercent, value.ToString("0.0#", culture));

    public static string Celsius(double value, CultureInfo culture) => Format(culture, ReportStrings.UnitCelsius, value.ToString("0", culture));

    /// <summary>Флаги по отдельности, в порядке перечисления; None — «нет».</summary>
    public static string Reasons(GpuThrottleReasons reasons) => reasons == GpuThrottleReasons.None
        ? EnumText.Format(reasons)
        : string.Join(ReportStrings.ListSeparator, Enum.GetValues<GpuThrottleReasons>()
            .Where(flag => flag != GpuThrottleReasons.None && reasons.HasFlag(flag))
            .Select(flag => EnumText.Format(flag)));

    private static ReportCell Temperature(Reading<double> temperature, Reading<double> limit, CultureInfo culture) =>
        temperature.IsAvailable && limit.IsAvailable
            ? Value(temperature, t => Format(culture, ReportStrings.TemperatureWithLimitFormat, Celsius(t, culture), Celsius(limit.Value, culture)))
            : Value(temperature, t => Celsius(t, culture));

    private static ReportCell Vram(Reading<long> used, Reading<long> total, CultureInfo culture) =>
        used.IsAvailable && total.IsAvailable
            ? Value(used, u => Format(culture, ReportStrings.VramUsageFormat, ByteSizeFormatter.Format(u, culture),
                ByteSizeFormatter.Format(total.Value, culture)))
            : Value(used, u => ByteSizeFormatter.Format(u, culture));

    private static string Format(CultureInfo culture, string format, params object[] args) => string.Format(culture, format, args);
}
