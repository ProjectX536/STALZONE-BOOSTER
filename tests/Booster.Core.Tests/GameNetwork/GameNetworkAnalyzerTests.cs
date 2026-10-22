using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.GameNetwork;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.GameNetwork;

public class GameNetworkAnalyzerTests
{
    private static readonly GameNetworkThresholds Thresholds = FakeConfiguration.DefaultGameNetworkThresholds;
    private static readonly NetworkThresholds Network = new(10, 100, 10, 2);

    private static GameNetworkMeasurement Measurement(TargetSeries[] series, RouteTrace? route = null) => new(
        GameNetworkMeasurement.CurrentSchemaVersion, "id", DateTimeOffset.UnixEpoch, TimeSpan.FromMilliseconds(500), null, null,
        new MeasuredServer("203.0.113.7", ServerSource.Etw, []), null, false, series, route,
        Reading<WifiStatus>.Unknown("wlan"), Reading<IReadOnlyList<ConsumerTraffic>>.Unknown("etw"), [],
        Reading<double>.Unknown("self"), MeasurementEnd.Completed);

    private static double?[] Samples(int count) => Enumerable.Range(1, count).Select(i => (double?)i).ToArray();

    [Fact]
    public void Targets_are_summarized_with_p95()
    {
        var analysis = GameNetworkAnalyzer.Analyze(Measurement(
        [
            new TargetSeries(NetworkTargetKind.Gateway, "192.168.1.1", Samples(10)),
            new TargetSeries(NetworkTargetKind.Host, "203.0.113.7", Samples(100)),
        ]), Thresholds, Network);

        Assert.Equal(2, analysis.Targets.Count);
        Assert.Equal(NetworkTargetKind.Gateway, analysis.Targets[0].Summary.Kind);
        Assert.Equal(10, analysis.Targets[0].Summary.Received);
        Assert.Equal(DataStatus.Unknown, analysis.Targets[0].P95Ms.Status);
        Assert.Equal(NetworkTargetKind.Host, analysis.Targets[1].Summary.Kind);
        Assert.True(analysis.Targets[1].P95Ms.IsAvailable);
        Assert.Equal(95, analysis.Targets[1].P95Ms.Value);
    }

    [Fact]
    public void P95_counts_replies_only_and_uses_nearest_rank()
    {
        var samples = Samples(19).Append(null).Append(20).ToArray();

        var p95 = GameNetworkAnalyzer.P95(samples);

        Assert.True(p95.IsAvailable);
        Assert.Equal(19, p95.Value);
        Assert.Equal(DataStatus.Unknown, GameNetworkAnalyzer.P95(Samples(19)).Status);
        Assert.Equal(19, GameNetworkAnalyzer.P95(Samples(20)).Value);
    }

    [Fact]
    public void Last_hop_series_is_the_end()
    {
        var analysis = GameNetworkAnalyzer.Analyze(Measurement(
        [
            new TargetSeries(NetworkTargetKind.Gateway, "192.168.1.1", [1.0, 1.0, 1.0]),
            new TargetSeries(NetworkTargetKind.LastHop, "198.51.100.1", [40.0, 41.0, 42.0]),
        ]), Thresholds, Network);

        Assert.Equal(NetworkTargetKind.LastHop, analysis.Breakdown.EndKind);
        Assert.Equal(41, analysis.Breakdown.TotalMs.Value);
        Assert.Equal(1, analysis.Breakdown.HomeMs.Value);
    }

    [Fact]
    public void Host_series_wins_over_last_hop()
    {
        var analysis = GameNetworkAnalyzer.Analyze(Measurement(
        [
            new TargetSeries(NetworkTargetKind.LastHop, "198.51.100.1", [40.0, 41.0, 42.0]),
            new TargetSeries(NetworkTargetKind.Host, "203.0.113.7", [50.0, 50.0, 50.0]),
        ]), Thresholds, Network);

        Assert.Equal(NetworkTargetKind.Host, analysis.Breakdown.EndKind);
        Assert.Equal(50, analysis.Breakdown.TotalMs.Value);
    }

    [Fact]
    public void Isp_hop_splits_line_and_route()
    {
        var analysis = GameNetworkAnalyzer.Analyze(Measurement(
        [
            new TargetSeries(NetworkTargetKind.Gateway, "192.168.1.1", [2.0, 2.0, 2.0]),
            new TargetSeries(NetworkTargetKind.IspHop, "10.0.0.1", [10.0, 10.0, 10.0]),
            new TargetSeries(NetworkTargetKind.Host, "203.0.113.7", [40.0, 40.0, 40.0]),
        ]), Thresholds, Network);

        Assert.Equal(8, analysis.Breakdown.LineMs.Value);
        Assert.Equal(30, analysis.Breakdown.RouteMs.Value);
    }

    [Fact]
    public void Analysis_without_gateway_has_unknown_home_and_no_gateway_spikes()
    {
        var analysis = GameNetworkAnalyzer.Analyze(Measurement(
            [new TargetSeries(NetworkTargetKind.Host, "203.0.113.7", [40.0, 40.0, 40.0])]), Thresholds, Network);

        Assert.False(analysis.Breakdown.HomeMs.IsAvailable);
        Assert.Null(analysis.Spikes.Gateway);
    }

    [Fact]
    public void Analysis_uses_route_for_jump()
    {
        var route = new RouteTrace("203.0.113.7",
        [
            new RouteHop(1, "192.168.1.1", AddressScope.Private, [1.0, 1.0, 1.0]),
            new RouteHop(2, "10.0.0.1", AddressScope.Cgnat, [5.0, 5.0, 5.0]),
            new RouteHop(3, "198.51.100.1", AddressScope.Public, [60.0, 60.0, 60.0]),
            new RouteHop(4, "203.0.113.7", AddressScope.Public, [61.0, 61.0, 61.0]),
        ], true);

        var analysis = GameNetworkAnalyzer.Analyze(Measurement(
            [new TargetSeries(NetworkTargetKind.Host, "203.0.113.7", [61.0, 61.0, 61.0])], route), Thresholds, Network);

        Assert.NotNull(analysis.Jump);
        Assert.Equal(2, analysis.Jump.AfterTtl);
        Assert.Equal(3, analysis.Jump.AtTtl);
        Assert.Contains(analysis.Advice, a => a.Id == AdviceId.GameNetworkRouteJump);
    }

    [Fact]
    public void Analysis_without_route_has_no_jump()
    {
        var analysis = GameNetworkAnalyzer.Analyze(Measurement(
            [new TargetSeries(NetworkTargetKind.Host, "203.0.113.7", [61.0, 61.0, 61.0])]), Thresholds, Network);

        Assert.Null(analysis.Jump);
        Assert.DoesNotContain(analysis.Advice, a => a.Id == AdviceId.GameNetworkRouteJump);
    }

    [Fact]
    public void Advice_comes_from_the_advisor()
    {
        var analysis = GameNetworkAnalyzer.Analyze(Measurement(
            [new TargetSeries(NetworkTargetKind.Host, "203.0.113.7", [150.0, 150.0, 150.0])]), Thresholds, Network);

        var advice = Assert.Single(analysis.Advice);
        Assert.Equal(AdviceId.GameNetworkFarServer, advice.Id);
    }

    [Fact]
    public void Measurement_without_end_series_is_a_programming_error()
    {
        var measurement = Measurement([new TargetSeries(NetworkTargetKind.Gateway, "192.168.1.1", [1.0])]);

        Assert.Throws<ArgumentException>(() => GameNetworkAnalyzer.Analyze(measurement, Thresholds, Network));
    }
}
