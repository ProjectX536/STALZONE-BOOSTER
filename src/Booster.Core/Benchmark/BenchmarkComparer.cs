using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;

namespace Booster.Core.Benchmark;

public enum ComparedMetric
{
    AverageFps,
    OnePercentLowFps,
    FrameTimeP95Ms,
    FrameTimeP99Ms,
    StuttersPerMinute,
}

public enum ChangeVerdict
{
    Improved,
    Worsened,
    NoChange,
    Inconclusive,
}

public enum ComparisonCaveat
{
    TooFewRuns,
    ConditionsDiffer,
    ChangeNotDescribed,
    ThrottlingInSeries,
    HighRunVariation,
    NoValidRuns,
}

public sealed record MetricComparison(ComparedMetric Metric, MetricSpread Baseline, MetricSpread Candidate, double ChangePercent,
    ChangeVerdict Verdict);

public sealed record ConditionDifference(string Field, string? Baseline, string? Candidate);

public sealed record BenchmarkComparison(string BaselineId, string CandidateId, DateTimeOffset BaselineStartedAt,
    DateTimeOffset CandidateStartedAt, string? Change, bool Comparable, IReadOnlyList<MetricComparison> Metrics, IReadOnlyList<ConditionDifference> Differences, IReadOnlyList<ComparisonCaveat> Caveats, ChangeVerdict AverageFpsVerdict, ChangeVerdict OnePercentLowVerdict);

public sealed record BenchmarkReport(BenchmarkSeries Series, BenchmarkComparison? Comparison, IReadOnlyList<BenchmarkComparison> Chain,
    ComparisonSettings Settings);

public static class BenchmarkComparer
{
    public const string RenderAdapterField = "BenchmarkConditionRenderAdapter";
    public const string DriverField = "BenchmarkConditionDriver";
    public const string ResolutionField = "BenchmarkConditionResolution";
    public const string RefreshRateField = "BenchmarkConditionRefreshRate";
    public const string HdrField = "BenchmarkConditionHdr";
    public const string VrrField = "BenchmarkConditionVrr";
    public const string PowerPlanField = "BenchmarkConditionPowerPlan";
    public const string PowerSourceField = "BenchmarkConditionPowerSource";
    public const string ProfileField = "BenchmarkConditionProfile";
    public const string GraphicsProfileField = "BenchmarkConditionGraphicsProfile";
    public const string SceneField = "BenchmarkConditionScene";
    public const string WindowModeField = "BenchmarkConditionWindowMode";
    public const string PresentModesField = "BenchmarkConditionPresentModes";

    public const string FlagOn = "on";

    public const string FlagOff = "off";

    public static IReadOnlyList<string> ConditionFields { get; } =
    [
        RenderAdapterField, DriverField, ResolutionField, RefreshRateField, HdrField, VrrField, PowerPlanField, PowerSourceField, ProfileField,
        GraphicsProfileField, SceneField, WindowModeField, PresentModesField,
    ];

    public static BenchmarkComparison Compare(BenchmarkSeries baseline, BenchmarkSeries candidate, ComparisonSettings settings)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(settings);

        var change = string.IsNullOrWhiteSpace(candidate.Conditions.Notes.Change) ? null : candidate.Conditions.Notes.Change.Trim();
        if (!SameTarget(baseline, candidate))
            return new BenchmarkComparison(baseline.Id, candidate.Id, baseline.StartedAt, candidate.StartedAt, change, false, [], [], [],
                ChangeVerdict.Inconclusive, ChangeVerdict.Inconclusive);

        var differences = Differences(baseline.Conditions, candidate.Conditions);
        var caveats = new List<ComparisonCaveat>();
        IReadOnlyList<MetricComparison> metrics = [];
        if (baseline.Summary is { ValidRuns: > 0 } before && candidate.Summary is { ValidRuns: > 0 } after)
        {
            var tooFew = before.ValidRuns < settings.MinimumRuns || after.ValidRuns < settings.MinimumRuns;
            if (tooFew)
                caveats.Add(ComparisonCaveat.TooFewRuns);
            metrics =
            [
                Metric(ComparedMetric.AverageFps, before.AverageFps, after.AverageFps, tooFew, settings),
                Metric(ComparedMetric.OnePercentLowFps, before.OnePercentLowFps, after.OnePercentLowFps, tooFew, settings),
                Metric(ComparedMetric.FrameTimeP95Ms, before.FrameTimeP95Ms, after.FrameTimeP95Ms, tooFew, settings),
                Metric(ComparedMetric.FrameTimeP99Ms, before.FrameTimeP99Ms, after.FrameTimeP99Ms, tooFew, settings),
                Metric(ComparedMetric.StuttersPerMinute, before.StuttersPerMinute, after.StuttersPerMinute, tooFew, settings),
            ];
        }

        if (differences.Count > 0)
            caveats.Add(ComparisonCaveat.ConditionsDiffer);
        if (change is null)
            caveats.Add(ComparisonCaveat.ChangeNotDescribed);
        if (Throttled(baseline) || Throttled(candidate))
            caveats.Add(ComparisonCaveat.ThrottlingInSeries);
        if (new[] { baseline, candidate }.Any(s => s.Summary is { } summary && summary.AverageFpsVariationPercent > settings.RunVariationPercent))
            caveats.Add(ComparisonCaveat.HighRunVariation);
        if (!baseline.HasValidRuns || !candidate.HasValidRuns)
            caveats.Add(ComparisonCaveat.NoValidRuns);

        ChangeVerdict VerdictOf(ComparedMetric metric) =>
            metrics.FirstOrDefault(m => m.Metric == metric)?.Verdict ?? ChangeVerdict.Inconclusive;

        return new BenchmarkComparison(baseline.Id, candidate.Id, baseline.StartedAt, candidate.StartedAt, change, true, metrics,
            differences, caveats, VerdictOf(ComparedMetric.AverageFps), VerdictOf(ComparedMetric.OnePercentLowFps));
    }

    public static IReadOnlyList<BenchmarkComparison> Chain(IReadOnlyList<BenchmarkSeries> series, ComparisonSettings settings)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(settings);

        return series
            .Where(s => s.HasValidRuns)
            .GroupBy(s => s.Conditions.Target, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var ordered = group.OrderBy(s => s.StartedAt).ToArray();
                return ordered.Skip(1).Select((candidate, index) => (ordered[index], candidate));
            })
            .OrderBy(pair => pair.candidate.StartedAt)
            .Select(pair => Compare(pair.Item1, pair.candidate, settings))
            .ToArray();
    }

    private static bool SameTarget(BenchmarkSeries baseline, BenchmarkSeries candidate) =>
        string.Equals(baseline.Conditions.Target, candidate.Conditions.Target, StringComparison.OrdinalIgnoreCase);

    private static bool Throttled(BenchmarkSeries series) =>
        series.Bottleneck?.Findings.Any(f => f is { Class: BottleneckClass.ThermalThrottling, Confidence: >= AnalysisConfidence.Medium }) == true;

    private static MetricComparison Metric(ComparedMetric metric, MetricSpread baseline, MetricSpread candidate, bool tooFewRuns,
        ComparisonSettings settings)
    {
        var stutters = metric == ComparedMetric.StuttersPerMinute;
        var change = stutters
            ? candidate.Median - baseline.Median
            : baseline.Median > 0 ? (candidate.Median - baseline.Median) / baseline.Median * 100 : 0;
        var minimum = stutters ? settings.MinimumStutterChangePerMinute : settings.MinimumChangePercent;
        var higherIsBetter = metric is ComparedMetric.AverageFps or ComparedMetric.OnePercentLowFps;

        var verdict = tooFewRuns ? ChangeVerdict.Inconclusive
            : Math.Abs(change) < minimum ? ChangeVerdict.NoChange
            : baseline.Min <= candidate.Max && candidate.Min <= baseline.Max ? ChangeVerdict.Inconclusive
            : change > 0 == higherIsBetter ? ChangeVerdict.Improved
            : ChangeVerdict.Worsened;
        return new MetricComparison(metric, baseline, candidate, change, verdict);
    }

    private static IReadOnlyList<ConditionDifference> Differences(BenchmarkConditions baseline, BenchmarkConditions candidate)
    {
        var differences = new List<ConditionDifference>();

        void Measured(string field, string? before, string? after)
        {
            if (before is not null && after is not null && !string.Equals(before, after, StringComparison.Ordinal))
                differences.Add(new ConditionDifference(field, before, after));
        }

        void Noted(string field, string? before, string? after)
        {
            if (!string.IsNullOrWhiteSpace(before) && !string.IsNullOrWhiteSpace(after)
                && !string.Equals(before.Trim(), after.Trim(), StringComparison.OrdinalIgnoreCase))
                differences.Add(new ConditionDifference(field, before.Trim(), after.Trim()));
        }

        var beforeDisplay = baseline.Display.IsAvailable ? baseline.Display.Value : null;
        var afterDisplay = candidate.Display.IsAvailable ? candidate.Display.Value : null;

        Measured(RenderAdapterField, Known(baseline.RenderAdapter), Known(candidate.RenderAdapter));
        Measured(DriverField, Known(baseline.DriverVersion), Known(candidate.DriverVersion));
        Measured(ResolutionField, Resolution(beforeDisplay), Resolution(afterDisplay));
        Measured(RefreshRateField, beforeDisplay?.RefreshRateHz.ToString("0.##", CultureInfo.InvariantCulture),
            afterDisplay?.RefreshRateHz.ToString("0.##", CultureInfo.InvariantCulture));
        Measured(HdrField, Flag(beforeDisplay?.HdrEnabled), Flag(afterDisplay?.HdrEnabled));
        Measured(VrrField, Flag(beforeDisplay?.VrrEnabled), Flag(afterDisplay?.VrrEnabled));
        Measured(PowerPlanField, Known(baseline.PowerPlan), Known(candidate.PowerPlan));
        Measured(PowerSourceField, Source(baseline.PowerSource), Source(candidate.PowerSource));
        if (!string.Equals(baseline.ActiveProfile, candidate.ActiveProfile, StringComparison.Ordinal))
            differences.Add(new ConditionDifference(ProfileField, baseline.ActiveProfile, candidate.ActiveProfile));
        Noted(GraphicsProfileField, baseline.Notes.GraphicsProfile, candidate.Notes.GraphicsProfile);
        Noted(SceneField, baseline.Notes.Scene, candidate.Notes.Scene);
        Noted(WindowModeField, baseline.Notes.WindowMode, candidate.Notes.WindowMode);
        Measured(PresentModesField, Modes(baseline.PresentModes), Modes(candidate.PresentModes));
        return differences;
    }

    private static string? Known(Reading<string> reading) => reading.IsAvailable ? reading.Value : null;

    private static string? Resolution(DisplayMode? mode) =>
        mode is null ? null : string.Create(CultureInfo.InvariantCulture, $"{mode.Width}x{mode.Height}");

    private static string? Flag(bool? value) => value switch
    {
        true => FlagOn,
        false => FlagOff,
        null => null,
    };

    private static string? Source(PowerSource source) => source == PowerSource.Unknown ? null : source.ToString();

    private static string? Modes(IReadOnlyList<string> modes) =>
        modes.Count == 0 ? null : string.Join(", ", modes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
}
