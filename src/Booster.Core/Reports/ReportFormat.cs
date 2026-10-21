using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Inventory;
using Booster.Core.Resources;
using Booster.Core.Sensors;

namespace Booster.Core.Reports;

/// <summary>Помощники, общие для разделов отчёта: тексты значений, статусов и единиц в одной культуре.</summary>
internal static class ReportFormat
{
    public static ReportCell Text(string? text) => new(text ?? string.Empty);

    public static ReportCell Value<T>(Reading<T> reading, Func<T, string> format) => SensorFieldFormatter.Value(reading, format);

    public static ReportCell UnknownCell => Text(ReadingFormatter.StatusText(DataStatus.Unknown)) with { Tone = CellTone.Unavailable };

    /// <summary>Значения нет, потому что его не измеряли (прогон без метрик, сигнал без порога).</summary>
    public static ReportCell NotMeasuredCell => Text(ReportStrings.NotMeasured) with { Tone = CellTone.Unavailable };

    public static string Format(CultureInfo culture, string format, params object[] args) => string.Format(culture, format, args);

    public static string Number(int value, CultureInfo culture) => value.ToString(culture);

    public static string Milliseconds(double value, CultureInfo culture) =>
        Format(culture, ReportStrings.UnitMs, value.ToString("0.00", culture));

    /// <summary>Время хранится с исходным смещением или в UTC; человеку показывается местное.</summary>
    public static string Timestamp(DateTimeOffset value, CultureInfo culture) => value.ToLocalTime().ToString("G", culture);

    public static string Bool(bool value) => value ? ReportStrings.Yes : ReportStrings.No;

    /// <summary>Свободное место и его доля; без размера тома — только свободное место.</summary>
    public static ReportCell FreeSpace(VolumeInfo volume, CultureInfo culture)
    {
        if (volume.FreeBytes.IsAvailable && volume.SizeBytes is { IsAvailable: true, Value: var size and > 0 })
        {
            var free = volume.FreeBytes.Value;
            return Text(Format(culture, ReportStrings.FreeSpaceFormat, ByteSizeFormatter.Format(free, culture),
                ((double)free / size).ToString("P0", culture)));
        }
        return Value(volume.FreeBytes, v => ByteSizeFormatter.Format(v, culture));
    }

    /// <summary>Число метрики серии без единицы (она в названии метрики): FPS и рывки — до десятых, время кадра — до сотых.</summary>
    public static string MetricNumber(ComparedMetric metric, double value, CultureInfo culture) => metric switch
    {
        ComparedMetric.FrameTimeP95Ms or ComparedMetric.FrameTimeP99Ms => value.ToString("0.00", culture),
        _ => value.ToString("0.0", culture),
    };

    /// <summary>Диапазон прогонов «мин–макс».</summary>
    public static string Range(ComparedMetric metric, MetricSpread spread, CultureInfo culture) =>
        Format(culture, ReportStrings.SpreadFormat, MetricNumber(metric, spread.Min, culture), MetricNumber(metric, spread.Max, culture));

    /// <summary>Метрики итога серии в порядке <see cref="ComparedMetric"/>, как в таблице сравнения.</summary>
    public static IReadOnlyList<(ComparedMetric Metric, MetricSpread Spread)> SummaryMetrics(BenchmarkSummary summary) =>
    [
        (ComparedMetric.AverageFps, summary.AverageFps),
        (ComparedMetric.OnePercentLowFps, summary.OnePercentLowFps),
        (ComparedMetric.FrameTimeP95Ms, summary.FrameTimeP95Ms),
        (ComparedMetric.FrameTimeP99Ms, summary.FrameTimeP99Ms),
        (ComparedMetric.StuttersPerMinute, summary.StuttersPerMinute),
    ];

    /// <summary>
    /// Строка на находку; мало замеров — частоты не оцениваются, и это сказано прямо. «Признаков не найдено» — только
    /// когда замеров хватило.
    /// </summary>
    public static IReadOnlyList<ReportBlock> Throttling(ThrottleAssessment assessment, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        var blocks = new List<ReportBlock> { new ReportHeading(ReportStrings.ThrottleHeading) };
        if (assessment.Findings.Count > 0)
        {
            blocks.Add(new ReportTable(
                [ReportStrings.ColumnSignal, ReportStrings.ColumnConfidence, ReportStrings.ColumnAdapter, ReportStrings.ColumnSamples,
                    ReportStrings.ColumnPeak],
                [
                    .. assessment.Findings.Select(finding => new ReportRow(
                    [
                        Text(EnumText.Format(finding.Signal)) with
                        {
                            Tone = finding.Confidence >= ThrottleConfidence.Medium ? CellTone.Warning : CellTone.Normal,
                        },
                        Text(EnumText.Format(finding.Confidence)),
                        Text(finding.AdapterName ?? ReportStrings.ThrottleCpuSubject),
                        Text(Format(culture, ReportStrings.ThrottleSamplesFormat, Number(finding.Samples, culture),
                            Number(finding.TotalSamples, culture))),
                        Value(finding.Peak, peak => Peak(finding.Signal, peak, culture)),
                    ])),
                ]));
        }

        if (!assessment.EnoughSamples)
        {
            blocks.Add(new ReportParagraph(Text(Format(culture, ReportStrings.ThrottleTooFewSamplesFormat, Number(assessment.SampleCount, culture),
                Number(assessment.RequiredSamples, culture))) with { Tone = CellTone.Note }));
        }
        else if (assessment.Findings.Count == 0)
        {
            blocks.Add(new ReportParagraph(Text(ReportStrings.ThrottleNoneFound)));
        }

        return blocks;
    }

    /// <summary>Пик в единицах сигнала: температура, частота ядра или ограничение CPU в процентах.</summary>
    public static string Peak(ThrottleSignal signal, double value, CultureInfo culture) => signal switch
    {
        ThrottleSignal.GpuDriverThermal or ThrottleSignal.GpuTemperatureAtLimit => SensorFieldFormatter.Celsius(value, culture),
        ThrottleSignal.CpuPerformanceLimited => SensorFieldFormatter.Percent(value, culture),
        _ => SensorFieldFormatter.Mhz(value, culture),
    };
}
