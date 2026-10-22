using System.Net;
using Booster.Core.Abstractions;
using Booster.Core.GameNetwork;

namespace Booster.Core.Tests.GameNetwork;

public sealed class GameServerDetectorTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);
    private static readonly IReadOnlySet<int> Game = new HashSet<int> { 100 };

    private static TrafficFlow Flow(int pid, string ip, long events) => new(pid, IPAddress.Parse(ip), events, events * 100);

    private static TcpConnection Tcp(int pid, string ip, bool established = true) => new(pid, IPAddress.Parse(ip), 443, established);

    [Fact]
    public void Busiest_public_flow_of_game_comes_first()
    {
        var flows = new[]
        {
            Flow(100, "203.0.113.5", 640),
            Flow(100, "198.51.100.9", 500),
            Flow(100, "192.168.1.1", 900),
            Flow(200, "203.0.113.77", 5000),
        };

        var result = GameServerDetector.FromTraffic(flows, Game, Window, 10);

        Assert.Equal(
            [new ServerCandidate("203.0.113.5", 64), new ServerCandidate("198.51.100.9", 50)],
            result);
    }

    [Fact]
    public void Flows_below_threshold_are_not_candidates()
    {
        var result = GameServerDetector.FromTraffic([Flow(100, "203.0.113.5", 50)], Game, Window, 10);

        Assert.Empty(result);
    }

    [Fact]
    public void Same_remote_from_two_game_processes_is_summed()
    {
        var ids = new HashSet<int> { 100, 101 };
        var flows = new[] { Flow(100, "203.0.113.5", 60), Flow(101, "203.0.113.5", 60) };

        var result = GameServerDetector.FromTraffic(flows, ids, Window, 10);

        Assert.Equal([new ServerCandidate("203.0.113.5", 12)], result);
    }

    [Fact]
    public void Ipv4_mapped_remote_is_summed_with_plain_ipv4()
    {
        var flows = new[] { Flow(100, "203.0.113.5", 60), Flow(100, "::ffff:203.0.113.5", 60) };

        var result = GameServerDetector.FromTraffic(flows, Game, Window, 10);

        Assert.Equal([new ServerCandidate("203.0.113.5", 12)], result);
    }

    [Fact]
    public void At_most_three_candidates()
    {
        var flows = new[]
        {
            Flow(100, "203.0.113.1", 100),
            Flow(100, "203.0.113.2", 200),
            Flow(100, "203.0.113.3", 300),
            Flow(100, "203.0.113.4", 400),
        };

        var result = GameServerDetector.FromTraffic(flows, Game, Window, 1);

        Assert.Equal(["203.0.113.4", "203.0.113.3", "203.0.113.2"], result.Select(c => c.Address));
    }

    [Fact]
    public void Equal_rates_are_ordered_by_address()
    {
        var flows = new[] { Flow(100, "203.0.113.9", 100), Flow(100, "203.0.113.2", 100) };

        var result = GameServerDetector.FromTraffic(flows, Game, Window, 1);

        Assert.Equal(["203.0.113.2", "203.0.113.9"], result.Select(c => c.Address));
    }

    [Fact]
    public void Non_positive_window_gives_no_candidates()
    {
        var result = GameServerDetector.FromTraffic([Flow(100, "203.0.113.5", 100)], Game, TimeSpan.Zero, 1);

        Assert.Empty(result);
    }

    [Fact]
    public void Tcp_uses_established_public_connections_of_game()
    {
        var connections = new[]
        {
            Tcp(100, "203.0.113.5"),
            Tcp(100, "203.0.113.6", established: false),
            Tcp(100, "192.168.1.1"),
            Tcp(200, "203.0.113.77"),
        };

        var result = GameServerDetector.FromTcp(connections, Game);

        Assert.Equal([new ServerCandidate("203.0.113.5", null)], result);
    }

    [Fact]
    public void Tcp_orders_by_connection_count_then_address()
    {
        var connections = new[]
        {
            Tcp(100, "203.0.113.9"),
            Tcp(100, "203.0.113.2"),
            Tcp(100, "203.0.113.7"),
            Tcp(100, "203.0.113.7"),
        };

        var result = GameServerDetector.FromTcp(connections, Game);

        Assert.Equal(["203.0.113.7", "203.0.113.2", "203.0.113.9"], result.Select(c => c.Address));
    }

    [Fact]
    public void Ipv6_remote_is_supported()
    {
        var result = GameServerDetector.FromTraffic([Flow(100, "2001:db8::5", 640)], Game, Window, 10);

        Assert.Equal([new ServerCandidate("2001:db8::5", 64)], result);
    }
}
