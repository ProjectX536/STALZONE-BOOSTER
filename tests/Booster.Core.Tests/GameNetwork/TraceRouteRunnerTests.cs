using System.Net;
using Booster.Core.Abstractions;
using Booster.Core.GameNetwork;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.GameNetwork;

public sealed class TraceRouteRunnerTests
{
    private static readonly IPAddress Target = IPAddress.Parse("203.0.113.9");

    private readonly FakeTraceRoute _source = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private TraceRouteRunner Runner(int maxHops = 30, int probes = 3, int silent = 5) =>
        new(_source, new FakeConfiguration(gameNetwork: FakeConfiguration.DefaultGameNetwork with
        {
            TraceMaxHops = maxHops,
            TraceProbesPerHop = probes,
            TraceStopAfterSilentHops = silent,
        }));

    private void Reply(int ttl, string address, double ms, HopStatus status = HopStatus.TtlExpired) =>
        _source.Hops[ttl] = new HopReply(status, IPAddress.Parse(address), ms);

    [Fact]
    public async Task Trace_stops_at_destination()
    {
        Reply(1, "192.168.1.1", 1);
        Reply(2, "10.0.0.1", 5);
        Reply(3, "198.51.100.1", 9);
        Reply(4, "203.0.113.9", 12, HopStatus.Destination);
        Reply(5, "203.0.113.10", 13);

        var trace = await Runner().RunAsync(Target, Ct);

        Assert.Equal(4, trace.Hops.Count);
        Assert.True(trace.ReachedTarget);
        Assert.Equal("203.0.113.9", trace.Target);
        Assert.Equal([1, 2, 3, 4], trace.Hops.Select(h => h.Ttl));
        Assert.Equal(3, trace.Hops[0].ProbesMs.Count);
    }

    [Fact]
    public async Task Trace_stops_after_silent_hops()
    {
        Reply(1, "192.168.1.1", 1);
        Reply(2, "10.0.0.1", 5);

        var trace = await Runner(silent: 3).RunAsync(Target, Ct);

        Assert.Equal(2, trace.Hops.Count);
        Assert.False(trace.ReachedTarget);
    }

    [Fact]
    public async Task Trace_hop_scope_is_classified()
    {
        Reply(1, "192.168.1.1", 1);
        Reply(2, "100.64.0.1", 3);
        Reply(3, "198.51.100.1", 9);
        _source.Hops[4] = new HopReply(HopStatus.Timeout, null, null);
        Reply(5, "198.51.100.2", 11);

        var trace = await Runner(maxHops: 5).RunAsync(Target, Ct);

        Assert.Equal([AddressScope.Private, AddressScope.Cgnat, AddressScope.Public], trace.Hops.Take(3).Select(h => h.Scope));
        Assert.Equal("192.168.1.1", trace.Hops[0].Address);
        Assert.Null(trace.Hops[3].Address);
        Assert.Null(trace.Hops[3].Scope);
        Assert.All(trace.Hops[3].ProbesMs, p => Assert.Null(p));
        Assert.Equal(5, trace.Hops.Count);
    }

    [Fact]
    public async Task Trace_is_limited_by_max_hops()
    {
        for (var ttl = 1; ttl <= 10; ttl++)
            Reply(ttl, $"198.51.100.{ttl}", ttl);

        var trace = await Runner(maxHops: 6).RunAsync(Target, Ct);

        Assert.Equal(6, trace.Hops.Count);
        Assert.False(trace.ReachedTarget);
    }

    [Fact]
    public async Task Unreachable_with_address_is_a_response()
    {
        Reply(1, "192.168.1.1", 1, HopStatus.Unreachable);
        Reply(2, "10.0.0.1", 2);

        var trace = await Runner(silent: 1).RunAsync(Target, Ct);

        Assert.Equal(2, trace.Hops.Count);
    }

    [Fact]
    public async Task Local_icmp_failure_is_a_timeout()
    {
        var source = new ThrowingTraceRoute();
        var runner = new TraceRouteRunner(source, new FakeConfiguration(gameNetwork: FakeConfiguration.DefaultGameNetwork with
        {
            TraceStopAfterSilentHops = 2,
        }));

        var trace = await runner.RunAsync(Target, Ct);

        Assert.Empty(trace.Hops);
        Assert.False(trace.ReachedTarget);
    }

    [Fact]
    public async Task Probe_timeout_is_taken_from_network_settings()
    {
        var source = new ThrowingTraceRoute();
        var runner = new TraceRouteRunner(source, new FakeConfiguration(network: FakeConfiguration.DefaultNetwork with { PingTimeoutMs = 750 }));

        await runner.RunAsync(Target, Ct);

        Assert.Equal(TimeSpan.FromMilliseconds(750), source.LastTimeout);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runner().RunAsync(Target, cts.Token));
    }

    private sealed class ThrowingTraceRoute : ITraceRouteSource
    {
        public TimeSpan LastTimeout { get; private set; }

        public Task<HopReply> ProbeAsync(IPAddress target, int ttl, TimeSpan timeout, CancellationToken ct)
        {
            LastTimeout = timeout;
            throw new PingFailedException("icmp unavailable");
        }
    }
}
