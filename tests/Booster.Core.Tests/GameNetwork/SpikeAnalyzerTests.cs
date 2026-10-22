using Booster.Core.GameNetwork;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.GameNetwork;

public class SpikeAnalyzerTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private static readonly Booster.Core.Configuration.GameNetworkThresholds Thresholds = FakeConfiguration.DefaultGameNetworkThresholds;

    private static double?[] Series(int length, double background, double jitter, params (int Round, double Value)[] spikes)
    {
        var samples = new double?[length];
        for (var i = 0; i < length; i++)
            samples[i] = background + (i % 2 == 0 ? jitter : -jitter);
        foreach (var (round, value) in spikes)
            samples[round] = value;
        return samples;
    }

    private static double?[] SpikesAt(params int[] rounds) =>
        Series(180, 20, 1, rounds.Select(r => (r, 120.0)).ToArray());

    [Fact]
    public void Steady_series_has_no_spikes()
    {
        var result = SpikeAnalyzer.Analyze(Series(180, 20, 1), Interval, Thresholds);

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void Small_mad_uses_min_delta()
    {
        Assert.Empty(SpikeAnalyzer.SpikeRounds(Series(60, 20, 0, (30, 45)), 30, 5));
        Assert.Equal([30], SpikeAnalyzer.SpikeRounds(Series(60, 20, 0, (30, 55)), 30, 5));
    }

    [Fact]
    public void Timeouts_are_not_spikes()
    {
        var samples = Series(180, 20, 1);
        samples[10] = null;
        samples[50] = null;

        Assert.Equal(0, SpikeAnalyzer.Analyze(samples, Interval, Thresholds).Count);
    }

    [Fact]
    public void Adjacent_rounds_form_one_event()
    {
        var result = SpikeAnalyzer.Analyze(SpikesAt(10, 11, 70, 71, 130), Interval, Thresholds);

        Assert.Equal(5, result.Count);
        Assert.Equal([10, 70, 130], result.EventStarts);
    }

    [Fact]
    public void Regular_events_give_period()
    {
        var result = SpikeAnalyzer.Analyze(SpikesAt(10, 70, 130), Interval, Thresholds);

        Assert.True(result.PeriodSeconds.IsAvailable);
        Assert.Equal(60, result.PeriodSeconds.Value);
    }

    [Fact]
    public void Irregular_events_have_no_period()
    {
        var result = SpikeAnalyzer.Analyze(SpikesAt(10, 30, 130), Interval, Thresholds);

        Assert.False(result.PeriodSeconds.IsAvailable);
    }

    [Fact]
    public void Two_events_have_no_period()
    {
        var result = SpikeAnalyzer.Analyze(SpikesAt(10, 70), Interval, Thresholds);

        Assert.False(result.PeriodSeconds.IsAvailable);
    }

    [Fact]
    public void Gateway_spike_in_same_round_counts_at_home()
    {
        var gateway = new TargetSeries(NetworkTargetKind.Gateway, "192.168.0.1", SpikesAt(10, 70));
        var end = new TargetSeries(NetworkTargetKind.LastHop, "203.0.113.5", SpikesAt(10, 70, 100));

        var report = SpikeAnalyzer.Analyze(gateway, end, Interval, Thresholds);

        Assert.Equal(2, report.AtHome);
        Assert.Equal(1, report.BeyondGateway);
    }

    [Fact]
    public void No_gateway_series_leaves_place_unknown()
    {
        var end = new TargetSeries(NetworkTargetKind.LastHop, "203.0.113.5", SpikesAt(10, 70, 100));

        var report = SpikeAnalyzer.Analyze(null, end, Interval, Thresholds);

        Assert.Equal(0, report.AtHome);
        Assert.Equal(0, report.BeyondGateway);
        Assert.Null(report.Gateway);
    }
}
