using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.Benchmark;

public class BenchmarkComparerTests
{
    private const string Source = "test";

    private static readonly DateTimeOffset Start = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly MetricSpread Same = new(10, 10, 10);
    private static readonly BenchmarkNotes NoNotes = new(null, null, null, null);

    private static MetricSpread Spread(params double[] values)
    {
        var sorted = values.Order().ToArray();
        return new MetricSpread(sorted[sorted.Length / 2], sorted[0], sorted[^1]);
    }

    private static SeriesTelemetry Telemetry() =>
        new(30, Reading<string>.Known("GPU", Source), Reading<string>.Known("1.0", Source), Reading<double>.Unknown(),
            Reading<double>.Unknown(), Reading<double>.Unknown(), Reading<double>.Unknown(), Reading<double>.Unknown(), Reading<double>.Unknown(),
            Reading<double>.Unknown(), Reading<double>.Unknown(), Reading<double>.Unknown(), Reading<double>.Unknown(), Reading<double>.Unknown(),
            Reading<double>.Unknown(), [], null);

    private static BenchmarkConditions Conditions(string target = "game.exe", string driver = "32.0.15.6094", string? change = "до изменений") =>
        new(target, Reading<string>.Known("GPU", Source), Reading<string>.Known(driver, Source),
            Reading<DisplayMode>.Known(new DisplayMode("Monitor", 2560, 1440, 144, false, true), Source), Reading<string>.Known("Balanced", Source),
            PowerSource.AcPower, null, NoNotes with { Change = change }, ["Hardware: Independent Flip"], []);

    private static BenchmarkSeries Series(string id, double[] fps, BenchmarkConditions? conditions = null, int minutes = 0,
        MetricSpread? p95 = null, BottleneckAssessment? bottleneck = null)
    {
        BenchmarkSummary? summary = fps.Length == 0 ? null : Summary(Spread(fps));
        BenchmarkSummary Summary(MetricSpread averageFps) =>
            new(fps.Length, averageFps, Same, p95 ?? Same, Same, new MetricSpread(0, 0, 0), (averageFps.Max - averageFps.Min) / averageFps.Median * 100);
        return new BenchmarkSeries(id, Start.AddMinutes(minutes), new BenchmarkSchedule(3, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(5)), FrameCaptureStatus.Completed, null, conditions ?? Conditions(), [], summary, Telemetry(),
            bottleneck, [], null);
    }

    private static BenchmarkComparison Compare(BenchmarkSeries baseline, BenchmarkSeries candidate) =>
        BenchmarkComparer.Compare(baseline, candidate, FakeConfiguration.DefaultThresholds.Comparison);

    private static MetricComparison Metric(BenchmarkComparison comparison, ComparedMetric metric) =>
        Assert.Single(comparison.Metrics, m => m.Metric == metric);

    [Fact]
    public void Higher_fps_without_overlap_is_improved()
    {
        var comparison = Compare(Series("a", [100, 101, 102]), Series("b", [108, 109, 110]));

        var fps = Metric(comparison, ComparedMetric.AverageFps);
        Assert.Equal(7.92, fps.ChangePercent, 2);
        Assert.Equal(ChangeVerdict.Improved, fps.Verdict);
        Assert.Equal(ChangeVerdict.Improved, comparison.AverageFpsVerdict);
        Assert.Equal(ChangeVerdict.NoChange, comparison.OnePercentLowVerdict);
        Assert.True(comparison.Comparable);
        Assert.Empty(comparison.Caveats);
        Assert.Equal("до изменений", comparison.Change);
    }

    [Fact]
    public void Lower_fps_without_overlap_is_worsened()
    {
        Assert.Equal(ChangeVerdict.Worsened, Compare(Series("a", [108, 109, 110]), Series("b", [100, 101, 102])).AverageFpsVerdict);
    }

    [Fact]
    public void Small_difference_is_no_change()
    {
        var comparison = Compare(Series("a", [100, 100, 100]), Series("b", [102, 102, 102]));

        Assert.Equal(2, Metric(comparison, ComparedMetric.AverageFps).ChangePercent, 6);
        Assert.Equal(ChangeVerdict.NoChange, comparison.AverageFpsVerdict);
    }

    [Fact]
    public void Overlapping_ranges_are_inconclusive()
    {
        var comparison = Compare(Series("a", [95, 100, 105]), Series("b", [100, 105, 110]));

        Assert.Equal(ChangeVerdict.Inconclusive, comparison.AverageFpsVerdict);
        Assert.Contains(ComparisonCaveat.HighRunVariation, comparison.Caveats);
    }

    [Fact]
    public void Single_run_is_inconclusive_with_too_few_runs()
    {
        var comparison = Compare(Series("a", [100]), Series("b", [120, 121, 122]));

        Assert.All(comparison.Metrics, m => Assert.Equal(ChangeVerdict.Inconclusive, m.Verdict));
        Assert.Equal([ComparisonCaveat.TooFewRuns], comparison.Caveats);
    }

    [Fact]
    public void Frametime_lower_is_improved()
    {
        var comparison = Compare(Series("a", [100, 100, 100], p95: new MetricSpread(14, 13.8, 14.2)),
            Series("b", [100, 100, 100], p95: new MetricSpread(12, 11.8, 12.2)));

        var p95 = Metric(comparison, ComparedMetric.FrameTimeP95Ms);
        Assert.Equal(-14.29, p95.ChangePercent, 2);
        Assert.Equal(ChangeVerdict.Improved, p95.Verdict);
    }

    [Fact]
    public void Different_target_is_not_comparable()
    {
        var comparison = Compare(Series("a", [100, 101, 102]), Series("b", [150, 151, 152], Conditions(target: "other.exe")));

        Assert.False(comparison.Comparable);
        Assert.Empty(comparison.Metrics);
        Assert.Equal(ChangeVerdict.Inconclusive, comparison.AverageFpsVerdict);
    }

    [Fact]
    public void Target_comparison_ignores_case()
    {
        Assert.True(Compare(Series("a", [100, 101, 102]), Series("b", [100, 101, 102], Conditions(target: "GAME.EXE"))).Comparable);
    }

    [Fact]
    public void Driver_change_listed_in_differences_with_caveat()
    {
        var comparison = Compare(Series("a", [100, 101, 102]), Series("b", [100, 101, 102], Conditions(driver: "32.0.15.7000")));

        var difference = Assert.Single(comparison.Differences);
        Assert.Equal(new ConditionDifference(BenchmarkComparer.DriverField, "32.0.15.6094", "32.0.15.7000"), difference);
        Assert.Equal([ComparisonCaveat.ConditionsDiffer], comparison.Caveats);
    }

    [Fact]
    public void Unknown_condition_on_one_side_is_not_a_difference()
    {
        var candidate = Conditions() with { DriverVersion = Reading<string>.Unknown(Source) };

        Assert.Empty(Compare(Series("a", [100, 101, 102]), Series("b", [100, 101, 102], candidate)).Differences);
    }

    [Fact]
    public void Missing_change_description_adds_caveat()
    {
        var comparison = Compare(Series("a", [100, 101, 102]), Series("b", [100, 101, 102], Conditions(change: " ")));

        Assert.Equal([ComparisonCaveat.ChangeNotDescribed], comparison.Caveats);
    }

    [Fact]
    public void Throttling_in_candidate_adds_caveat()
    {
        var throttling = new BottleneckAssessment(
            [new BottleneckFinding(BottleneckClass.ThermalThrottling, AnalysisConfidence.Medium, [])], GpuLoadState.Normal, []);

        var comparison = Compare(Series("a", [100, 101, 102]), Series("b", [100, 101, 102], bottleneck: throttling));

        Assert.Equal([ComparisonCaveat.ThrottlingInSeries], comparison.Caveats);
    }

    [Fact]
    public void Series_without_valid_runs_compare_without_exception()
    {
        var comparison = Compare(Series("a", [100, 101, 102]), Series("b", []));

        Assert.True(comparison.Comparable);
        Assert.Empty(comparison.Metrics);
        Assert.Equal(ChangeVerdict.Inconclusive, comparison.AverageFpsVerdict);
        Assert.Equal(ChangeVerdict.Inconclusive, comparison.OnePercentLowVerdict);
        Assert.Equal([ComparisonCaveat.NoValidRuns], comparison.Caveats);
    }

    [Fact]
    public void Chain_compares_neighbours_of_same_target_in_time_order()
    {
        var first = Series("a1", [100, 101, 102], minutes: 0);
        var other = Series("x", [50, 51, 52], Conditions(target: "other.exe"), minutes: 5);
        var second = Series("a2", [108, 109, 110], minutes: 10);
        var empty = Series("a-empty", [], minutes: 15);
        var third = Series("a3", [109, 110, 111], minutes: 20);
        var otherLater = Series("y", [60, 61, 62], Conditions(target: "other.exe"), minutes: 25);

        var chain = BenchmarkComparer.Chain([third, otherLater, empty, second, other, first], FakeConfiguration.DefaultThresholds.Comparison);

        Assert.Equal([("a1", "a2"), ("a2", "a3"), ("x", "y")], chain.Select(c => (c.BaselineId, c.CandidateId)));
    }
}
