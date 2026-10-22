using Booster.Core.GameNetwork;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.GameNetwork;

public class RouteAnalyzerTests
{
    private static readonly double JumpMs = FakeConfiguration.DefaultGameNetworkThresholds.RouteJumpMs;

    private static RouteHop Hop(int ttl, double? median, string? address = null, AddressScope? scope = null) =>
        new(ttl, address, scope, median is null ? [null, null, null] : [median, median, median]);

    private static RouteTrace Trace(bool reached, params double?[] medians) =>
        new("203.0.113.9", medians.Select((m, i) => Hop(i + 1, m)).ToArray(), reached);

    [Fact]
    public void Sustained_jump_is_reported()
    {
        var jump = RouteAnalyzer.FindJump(Trace(true, 1, 8, 9, 40, 42, 41), JumpMs);

        Assert.NotNull(jump);
        Assert.Equal(3, jump.AfterTtl);
        Assert.Equal(4, jump.AtTtl);
        Assert.Equal(31, jump.IncreaseMs);
    }

    [Fact]
    public void Single_slow_hop_is_not_a_jump()
    {
        Assert.Null(RouteAnalyzer.FindJump(Trace(true, 1, 8, 60, 10, 11), JumpMs));
    }

    [Fact]
    public void Last_known_hop_without_destination_is_not_a_jump()
    {
        Assert.Null(RouteAnalyzer.FindJump(Trace(false, 1, 8, 50), JumpMs));
    }

    [Fact]
    public void Silent_hops_are_skipped()
    {
        var jump = RouteAnalyzer.FindJump(Trace(true, 1, 8, null, 40, 41), JumpMs);

        Assert.NotNull(jump);
        Assert.Equal(2, jump.AfterTtl);
        Assert.Equal(4, jump.AtTtl);
    }

    [Fact]
    public void Jump_on_destination_is_reported_when_target_reached()
    {
        var jump = RouteAnalyzer.FindJump(Trace(true, 1, 8, 50), JumpMs);

        Assert.NotNull(jump);
        Assert.Equal(3, jump.AtTtl);
    }

    [Fact]
    public void Isp_hop_is_first_public_or_cgnat()
    {
        var withCgnat = new RouteTrace("203.0.113.9",
        [
            Hop(1, 1, "192.168.1.1", AddressScope.Private),
            Hop(2, 2, "10.0.0.1", AddressScope.Private),
            Hop(3, 5, "100.70.1.1", AddressScope.Cgnat),
            Hop(4, 9, "203.0.113.1", AddressScope.Public),
        ], true);
        var withoutCgnat = withCgnat with { Hops = [withCgnat.Hops[0], withCgnat.Hops[1], withCgnat.Hops[3]] };
        var onlyPrivate = withCgnat with { Hops = [withCgnat.Hops[0], withCgnat.Hops[1]] };

        Assert.Equal(3, RouteAnalyzer.IspHop(withCgnat)?.Ttl);
        Assert.Equal(4, RouteAnalyzer.IspHop(withoutCgnat)?.Ttl);
        Assert.Null(RouteAnalyzer.IspHop(onlyPrivate));
    }

    [Fact]
    public void Isp_hop_skips_silent_and_unclassified_hops()
    {
        var trace = new RouteTrace("203.0.113.9",
        [
            Hop(1, 1, "192.168.1.1", AddressScope.Private),
            Hop(2, null, "203.0.113.2", AddressScope.Public),
            Hop(3, 4, "203.0.113.3", null),
            Hop(4, 6, "203.0.113.4", AddressScope.Public),
        ], true);

        Assert.Equal(4, RouteAnalyzer.IspHop(trace)?.Ttl);
    }

    [Fact]
    public void Hop_median_ignores_lost_probes_and_is_unknown_without_replies()
    {
        var hop = new RouteHop(1, null, null, [4, null, 8, 6]);

        Assert.Equal(6, RouteAnalyzer.HopMedian(hop).Value);
        Assert.False(RouteAnalyzer.HopMedian(Hop(2, null)).IsAvailable);
    }

    [Fact]
    public void Last_responding_is_last_hop_with_known_median()
    {
        Assert.Equal(3, RouteAnalyzer.LastResponding(Trace(false, 1, 8, 9, null, null))?.Ttl);
        Assert.Null(RouteAnalyzer.LastResponding(Trace(false, null, null)));
    }
}
