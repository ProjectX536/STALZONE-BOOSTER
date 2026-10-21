using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.Resources;
using static Booster.Core.Reports.ReportFormat;

namespace Booster.Core.Reports;

internal static class ComparisonReport
{
    public static ReportSection Build(BenchmarkComparison comparison, IReadOnlyList<BenchmarkComparison> chain, ComparisonSettings settings,
        CultureInfo culture)
    {
        var writer = new ReportBlockWriter();
        var worse = comparison.AverageFpsVerdict == ChangeVerdict.Worsened || comparison.OnePercentLowVerdict == ChangeVerdict.Worsened;
        writer.Paragraph(Text(Sentence(comparison, settings, culture)) with { Tone = worse ? CellTone.Warning : CellTone.Normal });
        writer.Fields(
        [
            (ReportStrings.ComparisonBaseline, Text(Timestamp(comparison.BaselineStartedAt, culture)) with { Tooltip = comparison.BaselineId }),
            (ReportStrings.ComparisonCandidate, Text(Timestamp(comparison.CandidateStartedAt, culture)) with { Tooltip = comparison.CandidateId }),
            (ReportStrings.ComparisonChange, Change(comparison.Change)),
        ]);

        Metrics(writer, comparison, culture);

        if (comparison.Differences.Count > 0)
        {
            writer.Heading(ReportStrings.ComparisonDifferencesHeading);
            writer.Table([ReportStrings.ColumnCondition, ReportStrings.ColumnBaseline, ReportStrings.ColumnCandidate],
                comparison.Differences.Select(difference => new ReportRow(
                [
                    Text(ReportStrings.ResourceManager.GetString(difference.Field, ReportStrings.Culture) ?? difference.Field),
                    ConditionValue(difference.Field, difference.Baseline, culture),
                    ConditionValue(difference.Field, difference.Candidate, culture),
                ])));
        }

        Caveats(writer, comparison);

        if (chain.Count > 0)
        {
            writer.Heading(ReportStrings.ComparisonChainHeading);
            writer.Table([ReportStrings.ColumnDate, ReportStrings.ColumnChange, ReportStrings.ColumnResult],
                chain.Select(step => new ReportRow(
                [
                    Text(Timestamp(step.CandidateStartedAt, culture)) with { Tooltip = step.CandidateId },
                    Change(step.Change),
                    Text(Sentence(step, settings, culture)),
                ])));
        }

        return new ReportSection(ReportSectionIds.Comparison, ReportStrings.SectionComparison, null, writer.Blocks);
    }

    public static void Metrics(ReportBlockWriter writer, BenchmarkComparison comparison, CultureInfo culture)
    {
        if (comparison.Metrics.Count == 0)
            return;

        writer.Table(
            [ReportStrings.ColumnMetric, ReportStrings.ColumnBaseline, ReportStrings.ColumnCandidate, ReportStrings.ColumnChange,
                ReportStrings.ColumnVerdict],
            comparison.Metrics.Select(metric => new ReportRow(
            [
                Text(EnumText.Format(metric.Metric)),
                Text(WithRange(metric.Metric, metric.Baseline, culture)),
                Text(WithRange(metric.Metric, metric.Candidate, culture)),
                Text(ChangeText(metric, culture)),
                Text(EnumText.Format(metric.Verdict)) with { Tone = metric.Verdict == ChangeVerdict.Worsened ? CellTone.Warning : CellTone.Normal },
            ])));
    }

    public static void Caveats(ReportBlockWriter writer, BenchmarkComparison comparison)
    {
        if (comparison.Caveats.Count == 0)
            return;

        writer.Heading(ReportStrings.ComparisonCaveatsHeading);
        foreach (var caveat in comparison.Caveats)
            writer.Paragraph(Text(EnumText.Format(caveat)) with { Tone = CellTone.Note });
    }

    public static string Sentence(BenchmarkComparison comparison, ComparisonSettings settings, CultureInfo culture)
    {
        if (!comparison.Comparable)
            return ReportStrings.ComparisonDifferentGames;
        if (comparison.Metrics.Count == 0)
            return ReportStrings.ComparisonNoMetrics;

        var fps = Percent(comparison, ComparedMetric.AverageFps, culture);
        var low = Percent(comparison, ComparedMetric.OnePercentLowFps, culture);
        return (comparison.AverageFpsVerdict, comparison.OnePercentLowVerdict) switch
        {
            (ChangeVerdict.Improved, ChangeVerdict.Worsened) => Format(culture, ReportStrings.ComparisonImprovedLowWorseFormat, fps, low),
            (ChangeVerdict.Improved, ChangeVerdict.Inconclusive) => Format(culture, ReportStrings.ComparisonImprovedLowUnclearFormat, fps),
            (ChangeVerdict.Improved, _) => Format(culture, ReportStrings.ComparisonImprovedFormat, fps),
            (ChangeVerdict.Worsened, _) => Format(culture, ReportStrings.ComparisonWorsenedFormat, fps),
            (_, ChangeVerdict.Worsened) => Format(culture, ReportStrings.ComparisonLowWorsenedFormat, low),
            (_, ChangeVerdict.Improved) => Format(culture, ReportStrings.ComparisonLowImprovedFormat, low),
            (ChangeVerdict.NoChange, _) => Format(culture, ReportStrings.ComparisonNoChangeFormat,
                settings.MinimumChangePercent.ToString("0.##", culture)),
            _ when comparison.Caveats.Contains(ComparisonCaveat.TooFewRuns) =>
                Format(culture, ReportStrings.ComparisonTooFewRunsFormat, Number(settings.MinimumRuns, culture)),
            _ => ReportStrings.ComparisonWithinSpread,
        };
    }

    private static string Percent(BenchmarkComparison comparison, ComparedMetric metric, CultureInfo culture) =>
        comparison.Metrics.FirstOrDefault(m => m.Metric == metric) is { } found
            ? Format(culture, ReportStrings.SignedPercentFormat, Signed(found.ChangePercent, culture))
            : ReportStrings.NotMeasured;

    private static string ChangeText(MetricComparison metric, CultureInfo culture) => metric.Metric == ComparedMetric.StuttersPerMinute
        ? Signed(metric.ChangePercent, culture)
        : Format(culture, ReportStrings.SignedPercentFormat, Signed(metric.ChangePercent, culture));

    private static string Signed(double value, CultureInfo culture) => value.ToString("+0.0;−0.0;0.0", culture);

    private static string WithRange(ComparedMetric metric, MetricSpread spread, CultureInfo culture) =>
        Format(culture, ReportStrings.SpreadWithMedianFormat, MetricNumber(metric, spread.Median, culture), Range(metric, spread, culture));

    private static ReportCell Change(string? change) => change is null
        ? Text(ReportStrings.ComparisonChangeNotDescribed) with { Tone = CellTone.Unavailable }
        : Text(change);

    private static ReportCell ConditionValue(string field, string? value, CultureInfo culture)
    {
        if (value is null)
            return field == BenchmarkComparer.ProfileField ? Text(ReportStrings.BenchmarkNoProfile) : UnknownCell;

        return field switch
        {
            BenchmarkComparer.HdrField or BenchmarkComparer.VrrField => Text(Bool(value == BenchmarkComparer.FlagOn)),
            BenchmarkComparer.PowerSourceField when Enum.TryParse<PowerSource>(value, out var source) => Text(EnumText.Format(source)),
            BenchmarkComparer.RefreshRateField when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var hz) =>
                Text(Format(culture, ReportStrings.UnitHz, hz.ToString("0.###", culture))),
            BenchmarkComparer.ResolutionField when value.Split('x') is [var width, var height] =>
                Text(Format(culture, ReportStrings.ResolutionFormat, width, height)),
            _ => Text(value),
        };
    }
}
