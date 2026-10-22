using System.Net;
using Booster.Core.Abstractions;
using Booster.Platform.Windows.Providers.GameNetwork;

namespace Booster.Platform.Windows.Tests.Providers.GameNetwork;

public class TrafficFlowAggregatorTests
{
    private static readonly IPAddress Local = IPAddress.Parse("192.168.1.10");
    private static readonly IPAddress Server = IPAddress.Parse("203.0.113.5");
    private static readonly IPAddress OtherServer = IPAddress.Parse("198.51.100.7");

    [Fact]
    public void Remote_is_the_non_local_address()
    {
        var aggregator = new TrafficFlowAggregator(16, new HashSet<IPAddress> { Local });

        aggregator.Add(new KernelNetworkEvent(1234, 120, Server, Local));
        aggregator.Add(new KernelNetworkEvent(1234, 80, Local, Server));

        Assert.Equal(new TrafficFlow(1234, Server, 2, 200), Assert.Single(aggregator.Snapshot()));
        Assert.Equal(0, aggregator.DroppedEvents);
    }

    [Fact]
    public void Flows_beyond_cap_are_dropped_and_counted()
    {
        var aggregator = new TrafficFlowAggregator(2, new HashSet<IPAddress> { Local });

        aggregator.Add(new KernelNetworkEvent(1, 10, Server, Local));
        aggregator.Add(new KernelNetworkEvent(2, 10, Server, Local));
        aggregator.Add(new KernelNetworkEvent(3, 10, Server, Local));
        aggregator.Add(new KernelNetworkEvent(1, 5, Local, Server));
        aggregator.Add(new KernelNetworkEvent(1, 10, OtherServer, Local));

        Assert.Equal([new TrafficFlow(1, Server, 2, 15), new TrafficFlow(2, Server, 1, 10)],
            aggregator.Snapshot().OrderBy(f => f.ProcessId));
        Assert.Equal(2, aggregator.DroppedEvents);
    }

    [Fact]
    public void Link_local_with_scope_is_local()
    {
        var aggregator = new TrafficFlowAggregator(16, new HashSet<IPAddress> { IPAddress.Parse("fe80::1%5") });
        var parsedLocal = new IPAddress(IPAddress.Parse("fe80::1").GetAddressBytes());
        var remote = IPAddress.Parse("2001:db8::5");

        aggregator.Add(new KernelNetworkEvent(1234, 60, parsedLocal, remote));

        Assert.Equal(new TrafficFlow(1234, remote, 1, 60), Assert.Single(aggregator.Snapshot()));
    }

    [Fact]
    public async Task Concurrent_writer_and_reader_keep_totals()
    {
        const int Events = 20_000;
        var aggregator = new TrafficFlowAggregator(16, new HashSet<IPAddress> { Local });
        var ct = TestContext.Current.CancellationToken;

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < Events; i++)
                aggregator.Add(new KernelNetworkEvent(i % 4, 1, i % 2 == 0 ? Server : OtherServer, Local));
        }, ct);
        while (!writer.IsCompleted)
            _ = aggregator.Snapshot();
        await writer;

        var flows = aggregator.Snapshot();
        Assert.Equal(4, flows.Count);
        Assert.Equal(Events, flows.Sum(f => f.Events));
        Assert.Equal(Events, flows.Sum(f => f.Bytes));
    }
}
