using System.Net;
using System.Net.NetworkInformation;
using Booster.Core.Abstractions;
using Booster.Core.GameNetwork;
using Booster.Platform.Windows.Providers.GameNetwork;

namespace Booster.Platform.Windows.Tests.Providers.GameNetwork;

public class TraceRouteSourceTests
{
    private static readonly TimeSpan Elapsed = TimeSpan.FromMilliseconds(7);

    [Theory]
    [InlineData(IPStatus.Success, HopStatus.Destination)]
    [InlineData(IPStatus.TtlExpired, HopStatus.TtlExpired)]
    [InlineData(IPStatus.TimeExceeded, HopStatus.TtlExpired)]
    [InlineData(IPStatus.TimedOut, HopStatus.Timeout)]
    [InlineData(IPStatus.DestinationHostUnreachable, HopStatus.Unreachable)]
    [InlineData(IPStatus.DestinationNetworkUnreachable, HopStatus.Unreachable)]
    [InlineData(IPStatus.DestinationPortUnreachable, HopStatus.Unreachable)]
    [InlineData(IPStatus.DestinationProhibited, HopStatus.Unreachable)]
    public void Ip_status_maps_to_hop_status(IPStatus status, HopStatus expected) =>
        Assert.Equal(expected, TraceRouteSource.Map(status));

    [Theory]
    [InlineData(IPStatus.NoResources)]
    [InlineData(IPStatus.HardwareError)]
    [InlineData(IPStatus.BadDestination)]
    [InlineData(IPStatus.Unknown)]
    [InlineData(IPStatus.PacketTooBig)]
    public void Unlisted_status_does_not_map(IPStatus status) => Assert.Null(TraceRouteSource.Map(status));

    [Fact]
    public void Unlisted_status_is_a_failed_ping() =>
        Assert.Throws<PingFailedException>(() => TraceRouteSource.ToHop(IPStatus.NoResources, null, 0, Elapsed));

    [Fact]
    public void Unspecified_address_becomes_null()
    {
        Assert.Null(TraceRouteSource.ToHop(IPStatus.TtlExpired, IPAddress.Any, 5, Elapsed).Address);
        Assert.Null(TraceRouteSource.ToHop(IPStatus.TtlExpired, IPAddress.IPv6Any, 5, Elapsed).Address);
    }

    [Fact]
    public void Success_keeps_zero_milliseconds() =>
        Assert.Equal(0, TraceRouteSource.ToHop(IPStatus.Success, IPAddress.Loopback, 0, Elapsed).RoundTripMs);

    [Fact]
    public void Ttl_expired_with_zero_uses_elapsed_time() =>
        Assert.Equal(7, TraceRouteSource.ToHop(IPStatus.TtlExpired, IPAddress.Parse("10.0.0.1"), 0, Elapsed).RoundTripMs);

    [Fact]
    public void Ttl_expired_keeps_reported_time() =>
        Assert.Equal(3, TraceRouteSource.ToHop(IPStatus.TtlExpired, IPAddress.Parse("10.0.0.1"), 3, Elapsed).RoundTripMs);

    [Fact]
    public void Timeout_has_no_address_or_time()
    {
        var hop = TraceRouteSource.ToHop(IPStatus.TimedOut, IPAddress.Loopback, 0, Elapsed);

        Assert.Equal(HopStatus.Timeout, hop.Status);
        Assert.Null(hop.Address);
        Assert.Null(hop.RoundTripMs);
    }
}
