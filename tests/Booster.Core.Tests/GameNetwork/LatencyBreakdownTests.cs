using Booster.Core.Abstractions;
using Booster.Core.GameNetwork;
using Booster.Core.SystemHealth;

namespace Booster.Core.Tests.GameNetwork;

public class LatencyBreakdownTests
{
    private static NetworkTargetResult Target(NetworkTargetKind kind, double? median) =>
        new(kind, "203.0.113.1", 10, median is null ? 0 : 10,
            median is null ? Reading<double>.Unknown() : Reading<double>.Known(median.Value, "test"),
            Reading<double>.Unknown(), Reading<double>.Unknown());

    private static NetworkTargetResult Gateway(double? m) => Target(NetworkTargetKind.Gateway, m);
    private static NetworkTargetResult Isp(double? m) => Target(NetworkTargetKind.IspHop, m);
    private static NetworkTargetResult Server(double? m) => Target(NetworkTargetKind.Host, m);

    [Fact]
    public void Breakdown_from_medians()
    {
        var result = LatencyBreakdownCalculator.Compute(Gateway(2), Isp(9), Server(55));

        Assert.Equal(2, result.HomeMs.Value);
        Assert.Equal(7, result.LineMs.Value);
        Assert.Equal(46, result.RouteMs.Value);
        Assert.Equal(55, result.TotalMs.Value);
        Assert.False(result.Clamped);
        Assert.Equal(NetworkTargetKind.Host, result.EndKind);
    }

    [Fact]
    public void Negative_difference_is_clamped()
    {
        var result = LatencyBreakdownCalculator.Compute(Gateway(3), Isp(2), Server(55));

        Assert.Equal(0, result.LineMs.Value);
        Assert.True(result.LineMs.IsAvailable);
        Assert.True(result.Clamped);
    }

    [Fact]
    public void No_isp_hop_leaves_line_and_route_unknown()
    {
        var result = LatencyBreakdownCalculator.Compute(Gateway(2), null, Server(55));

        Assert.Equal(2, result.HomeMs.Value);
        Assert.False(result.LineMs.IsAvailable);
        Assert.False(result.RouteMs.IsAvailable);
        Assert.Equal(55, result.TotalMs.Value);
        Assert.False(result.Clamped);
    }

    [Fact]
    public void Silent_end_gives_unknown_total()
    {
        var result = LatencyBreakdownCalculator.Compute(Gateway(2), Isp(9), Server(null));

        Assert.False(result.TotalMs.IsAvailable);
        Assert.False(result.RouteMs.IsAvailable);
        Assert.Equal(7, result.LineMs.Value);
    }

    [Fact]
    public void Missing_gateway_leaves_home_and_line_unknown_but_route_and_total_known()
    {
        var result = LatencyBreakdownCalculator.Compute(null, Isp(9), Server(55));

        Assert.False(result.HomeMs.IsAvailable);
        Assert.False(result.LineMs.IsAvailable);
        Assert.Equal(46, result.RouteMs.Value);
        Assert.Equal(55, result.TotalMs.Value);
    }

    [Fact]
    public void Negative_route_is_clamped()
    {
        var result = LatencyBreakdownCalculator.Compute(Gateway(2), Isp(60), Server(55));

        Assert.Equal(0, result.RouteMs.Value);
        Assert.True(result.Clamped);
    }
}
