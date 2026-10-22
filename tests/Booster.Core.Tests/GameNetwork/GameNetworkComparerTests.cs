using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.GameNetwork;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.GameNetwork;

public class GameNetworkComparerTests
{
    private static readonly GameNetworkThresholds Thresholds = FakeConfiguration.DefaultGameNetworkThresholds;
    private static readonly NetworkThresholds Network = new(10, 100, 10, 2);

    private static NetworkAdapterInfo Adapter(NetworkMedium medium) => new("a", "a", "a", medium, null, []);

    private static GameNetworkMeasurement Run(double?[] samples, string server = "203.0.113.7", string? label = null,
        string[]? actions = null, NetworkMedium? medium = NetworkMedium.Ethernet, MeasurementEnd end = MeasurementEnd.Completed,
        int startedMinute = 0) => new(
        GameNetworkMeasurement.CurrentSchemaVersion, "id", DateTimeOffset.UnixEpoch.AddMinutes(startedMinute), TimeSpan.FromMilliseconds(500),
        null, label, new MeasuredServer(server, ServerSource.Etw, []), medium is null ? null : Adapter(medium.Value), false,
        [new TargetSeries(NetworkTargetKind.Host, server, samples)], null, Reading<WifiStatus>.Unknown("wlan"),
        Reading<IReadOnlyList<ConsumerTraffic>>.Unknown("etw"), actions ?? [], Reading<double>.Unknown("self"), end);

    private static double?[] Flat(double ms, int count = 100) => Enumerable.Repeat((double?)ms, count).ToArray();

    private static double?[] WithSpikes(int spikes)
    {
        var samples = Flat(40, 120);
        for (var i = 0; i < spikes; i++)
            samples[20 + i * 40] = 200;
        return samples;
    }

    private static MeasurementGroup Group(params GameNetworkMeasurement[] runs) =>
        new(runs[0].Server.Address, runs[0].Label, runs[0].ActiveActions, runs);

    private static MeasurementGroup Medians(params double[] medians) => Group(medians.Select(m => Run(Flat(m))).ToArray());

    private static NetworkMetricComparison Metric(GameNetworkComparison c, NetworkMetric metric) => c.Metrics.Single(m => m.Metric == metric);

    private static GameNetworkComparison Compare(MeasurementGroup baseline, MeasurementGroup candidate) =>
        GameNetworkComparer.Compare(baseline, candidate, Thresholds, Network);

    [Fact]
    public void Single_run_side_is_inconclusive()
    {
        var comparison = Compare(Medians(55), Medians(40, 41));

        Assert.Contains(ComparisonCaveat.TooFewRuns, comparison.Caveats);
        Assert.Equal(ChangeVerdict.Inconclusive, comparison.Verdict);
        Assert.Equal(1, comparison.BaselineRuns);
        Assert.Equal(2, comparison.CandidateRuns);
    }

    [Fact]
    public void Clear_median_drop_is_improved()
    {
        var comparison = Compare(Medians(55, 56), Medians(40, 41));

        var median = Metric(comparison, NetworkMetric.MedianMs);
        Assert.Equal(ChangeVerdict.Improved, median.Verdict);
        Assert.Equal(-15, median.Change);
        Assert.Equal(55.5, median.Baseline);
        Assert.Equal(40.5, median.Candidate);
        Assert.Equal(ChangeVerdict.Improved, comparison.Verdict);
        Assert.Equal("203.0.113.7", comparison.Server);
    }

    [Fact]
    public void Clear_median_rise_is_worsened()
    {
        var comparison = Compare(Medians(40, 41), Medians(55, 56));

        Assert.Equal(ChangeVerdict.Worsened, Metric(comparison, NetworkMetric.MedianMs).Verdict);
        Assert.Equal(ChangeVerdict.Worsened, comparison.Verdict);
    }

    [Fact]
    public void Silent_run_has_no_spikes_value()
    {
        var baseline = Group(Run(WithSpikes(3)), Run(WithSpikes(3)));
        var candidate = Group(Run(WithSpikes(0)), Run(new double?[] { null, null, null }));

        var comparison = Compare(baseline, candidate);

        Assert.Contains(ComparisonCaveat.TooFewRuns, comparison.Caveats);
        Assert.Equal(ChangeVerdict.Inconclusive, Metric(comparison, NetworkMetric.SpikesPerMinute).Verdict);
        Assert.Equal(ChangeVerdict.Inconclusive, comparison.Verdict);
    }

    [Fact]
    public void Small_change_is_no_change()
    {
        var comparison = Compare(Medians(55, 56), Medians(54, 55));

        Assert.Equal(ChangeVerdict.NoChange, Metric(comparison, NetworkMetric.MedianMs).Verdict);
        Assert.Equal(ChangeVerdict.NoChange, comparison.Verdict);
    }

    [Fact]
    public void Overlapping_runs_are_inconclusive()
    {
        var comparison = Compare(Medians(40, 70), Medians(30, 50));

        Assert.Equal(ChangeVerdict.Inconclusive, Metric(comparison, NetworkMetric.MedianMs).Verdict);
        Assert.Contains(ComparisonCaveat.HighRunVariation, comparison.Caveats);
        Assert.Equal(ChangeVerdict.Inconclusive, comparison.Verdict);
    }

    [Fact]
    public void Different_medium_is_not_comparable()
    {
        var baseline = Group(Run(Flat(55), medium: NetworkMedium.Wireless), Run(Flat(56), medium: NetworkMedium.Wireless));
        var candidate = Group(Run(Flat(40), medium: NetworkMedium.Ethernet), Run(Flat(41), medium: NetworkMedium.Ethernet));

        var comparison = Compare(baseline, candidate);

        Assert.Contains(ComparisonCaveat.ConditionsDiffer, comparison.Caveats);
        Assert.Equal(ChangeVerdict.Inconclusive, comparison.Verdict);
        Assert.Equal(ChangeVerdict.Improved, Metric(comparison, NetworkMetric.MedianMs).Verdict);
    }

    [Fact]
    public void Different_server_is_not_comparable()
    {
        var baseline = Group(Run(Flat(55)), Run(Flat(56)));
        var candidate = Group(Run(Flat(40), server: "203.0.113.8"), Run(Flat(41), server: "203.0.113.8"));

        var comparison = Compare(baseline, candidate);

        Assert.Contains(ComparisonCaveat.ConditionsDiffer, comparison.Caveats);
        Assert.Equal(ChangeVerdict.Inconclusive, comparison.Verdict);
    }

    [Fact]
    public void Spikes_per_minute_drop_is_improved()
    {
        var baseline = Group(Run(WithSpikes(3)), Run(WithSpikes(3)));
        var candidate = Group(Run(WithSpikes(0)), Run(WithSpikes(0)));

        var comparison = Compare(baseline, candidate);

        var spikes = Metric(comparison, NetworkMetric.SpikesPerMinute);
        Assert.Equal(3, spikes.Baseline);
        Assert.Equal(0, spikes.Candidate);
        Assert.Equal(-3, spikes.Change);
        Assert.Equal(ChangeVerdict.Improved, spikes.Verdict);
        Assert.Equal(ChangeVerdict.NoChange, Metric(comparison, NetworkMetric.MedianMs).Verdict);
    }

    [Fact]
    public void Zero_on_both_sides_is_no_change_for_loss()
    {
        var comparison = Compare(Medians(40, 41), Medians(40, 41));

        Assert.Equal(ChangeVerdict.NoChange, Metric(comparison, NetworkMetric.LossPercent).Verdict);
        Assert.Equal(ChangeVerdict.NoChange, comparison.Verdict);
    }

    [Fact]
    public void Growth_from_zero_loss_is_worsened()
    {
        var lossy = Flat(40).Select((s, i) => i % 10 == 0 ? null : s).ToArray();
        var comparison = Compare(Medians(40, 41), Group(Run(lossy), Run(lossy)));

        var loss = Metric(comparison, NetworkMetric.LossPercent);
        Assert.Equal(10, loss.Change);
        Assert.Equal(ChangeVerdict.Worsened, loss.Verdict);
    }

    [Fact]
    public void Run_without_known_value_is_skipped_and_counts_toward_too_few_runs()
    {
        var silent = Run(new double?[] { null, null, null });
        var comparison = Compare(Medians(55, 56), Group(Run(Flat(40)), silent));

        Assert.Contains(ComparisonCaveat.TooFewRuns, comparison.Caveats);
        Assert.Equal(ChangeVerdict.Inconclusive, Metric(comparison, NetworkMetric.MedianMs).Verdict);
    }

    [Fact]
    public void Group_excludes_game_exited_runs()
    {
        var groups = GameNetworkComparer.Group([Run(Flat(40)), Run(Flat(41), end: MeasurementEnd.GameExited)]);

        var group = Assert.Single(groups);
        Assert.Single(group.Runs);
    }

    [Fact]
    public void Group_key_uses_label_and_actions()
    {
        var runs = new[]
        {
            Run(Flat(40), startedMinute: 1),
            Run(Flat(40), label: "кабель", startedMinute: 2),
            Run(Flat(40), label: "кабель", actions: ["wifi-low-latency"], startedMinute: 3),
            Run(Flat(40), label: "кабель", actions: ["b", "a"], startedMinute: 4),
            Run(Flat(40), label: "кабель", actions: ["a", "b"], startedMinute: 5),
        };

        var groups = GameNetworkComparer.Group(runs);

        Assert.Equal(4, groups.Count);
        var ab = groups.Single(g => g.ActiveActions.SequenceEqual(["a", "b"]));
        Assert.Equal(2, ab.Runs.Count);
        Assert.Same(ab, groups[0]);
    }

    [Fact]
    public void Group_treats_null_and_empty_label_alike_and_orders_runs_by_start()
    {
        var late = Run(Flat(40), label: "", startedMinute: 9);
        var early = Run(Flat(41), startedMinute: 1);

        var group = Assert.Single(GameNetworkComparer.Group([late, early]));

        Assert.Equal([early, late], group.Runs);
        Assert.Null(group.Label);
    }
}
