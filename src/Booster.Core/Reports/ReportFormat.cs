using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Inventory;
using Booster.Core.Resources;
using Booster.Core.Sensors;

namespace Booster.Core.Reports;

internal static class ReportFormat
{
    public static ReportCell Text(string? text) => new(text ?? string.Empty);

    public static ReportCell Value<T>(Reading<T> reading, Func<T, string> format) => SensorFieldFormatter.Value(reading, format);

    public static ReportCell UnknownCell => Text(ReadingFormatter.StatusText(DataStatus.Unknown)) with { Tone = CellTone.Unavailable };

    public static ReportCell NotMeasuredCell => Text(ReportStrings.NotMeasured) with { Tone = CellTone.Unavailable };

    public static string Format(CultureInfo culture, string format, params object[] args) => string.Format(culture, format, args);

    public static string Number(int value, CultureInfo culture) => value.ToString(culture);

    public static string Milliseconds(double value, CultureInfo culture) =>
        Format(culture, ReportStrings.UnitMs, value.ToString("0.00", culture));

    public static string Timestamp(DateTimeOffset value, CultureInfo culture) => value.ToLocalTime().ToString("G", culture);

    public static string Bool(bool value) => value ? ReportStrings.Yes : ReportStrings.No;

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

    public static string MetricNumber(ComparedMetric metric, double value, CultureInfo culture) => metric switch
    {
        ComparedMetric.FrameTimeP95Ms or ComparedMetric.FrameTimeP99Ms => value.ToString("0.00", culture),
        _ => value.ToString("0.0", culture),
    };

    public static string Range(ComparedMetric metric, MetricSpread spread, CultureInfo culture) =>
        Format(culture, ReportStrings.SpreadFormat, MetricNumber(metric, spread.Min, culture), MetricNumber(metric, spread.Max, culture));

    public static IReadOnlyList<(ComparedMetric Metric, MetricSpread Spread)> SummaryMetrics(BenchmarkSummary summary) =>
    [
        (ComparedMetric.AverageFps, summary.AverageFps),
        (ComparedMetric.OnePercentLowFps, summary.OnePercentLowFps),
        (ComparedMetric.FrameTimeP95Ms, summary.FrameTimeP95Ms),
        (ComparedMetric.FrameTimeP99Ms, summary.FrameTimeP99Ms),
        (ComparedMetric.StuttersPerMinute, summary.StuttersPerMinute),
    ];

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

    public static string Peak(ThrottleSignal signal, double value, CultureInfo culture) => signal switch
    {
        ThrottleSignal.GpuDriverThermal or ThrottleSignal.GpuTemperatureAtLimit => SensorFieldFormatter.Celsius(value, culture),
        ThrottleSignal.CpuPerformanceLimited => SensorFieldFormatter.Percent(value, culture),
        _ => SensorFieldFormatter.Mhz(value, culture),
    };
}
