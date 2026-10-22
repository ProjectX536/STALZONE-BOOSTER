using System.Net;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.GameNetwork;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.GameNetwork;

public sealed class ConsumerResolverTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);
    private static readonly IPAddress Remote = IPAddress.Parse("198.51.100.7");

    private readonly FakeProcessProvider _processes = new();
    private readonly List<ServiceInfo> _services = [];
    private int _reads;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TrafficFlow Flow(int pid, long bytes) => new(pid, Remote, 10, bytes);

    private static ServiceInfo Service(string name, int? pid) =>
        new(name, name, ServiceRunState.Running, ServiceStartMode.Automatic, false, null, pid, Reading<string>.Unknown(), []);

    private Task<IReadOnlyList<ConsumerTraffic>> Resolve(IReadOnlyList<TrafficFlow> flows, IReadOnlySet<int>? game = null,
        FakeConfiguration? config = null) =>
        new ConsumerResolver(_processes, new FakeServiceInventorySource(_ =>
        {
            _reads++;
            return Task.FromResult<IReadOnlyList<ServiceInfo>>(_services.ToArray());
        }), config ?? new FakeConfiguration()).ResolveAsync(flows, game ?? new HashSet<int>(), Window, Ct);

    [Fact]
    public async Task Svchost_flow_is_named_by_service()
    {
        _processes.Start("svchost.exe", 900);
        _services.Add(Service("DoSvc", 900));
        _services.Add(Service("BITS", 900));

        var result = await Resolve([Flow(900, 10 * 1024 * 1024)]);

        var consumer = Assert.Single(result);
        Assert.Equal(900, consumer.ProcessId);
        Assert.Equal("svchost.exe", consumer.Name);
        Assert.Equal(NetworkConsumerKind.DeliveryOptimization, consumer.Kind);
        Assert.Equal(["BITS", "DoSvc"], consumer.Services);
        Assert.Equal(1024, consumer.KBytesPerSecond);
        Assert.Equal(1, _reads);
    }

    [Fact]
    public async Task Known_process_gets_catalog_kind()
    {
        var catalog = FakeConfiguration.DefaultCatalog with
        {
            NetworkConsumers = [new("qbittorrent.exe", null, "qBittorrent", NetworkConsumerKind.Downloader)],
        };
        _processes.Start("qBittorrent", 100);
        _processes.Start("qbittorrent.exe", 101);

        var result = await Resolve([Flow(100, 2048), Flow(101, 1024)], config: new FakeConfiguration(catalog: catalog));

        Assert.Equal(2, result.Count);
        Assert.All(result, c => Assert.Equal(NetworkConsumerKind.Downloader, c.Kind));
    }

    [Fact]
    public async Task Service_match_wins_over_process_name()
    {
        var catalog = FakeConfiguration.DefaultCatalog with
        {
            NetworkConsumers =
            [
                new("svchost.exe", null, "svchost", NetworkConsumerKind.CloudSync),
                new(null, "DoSvc", "Delivery", NetworkConsumerKind.DeliveryOptimization),
            ],
        };
        _processes.Start("svchost.exe", 900);
        _services.Add(Service("DoSvc", 900));

        var result = await Resolve([Flow(900, 1024)], config: new FakeConfiguration(catalog: catalog));

        Assert.Equal(NetworkConsumerKind.DeliveryOptimization, Assert.Single(result).Kind);
    }

    [Fact]
    public async Task Service_name_match_ignores_case()
    {
        _processes.Start("svchost.exe", 900);
        _services.Add(Service("dosvc", 900));

        var result = await Resolve([Flow(900, 1024)]);

        Assert.Equal(NetworkConsumerKind.DeliveryOptimization, Assert.Single(result).Kind);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Non_positive_window_is_rejected_before_any_provider_call(int seconds)
    {
        var resolver = new ConsumerResolver(_processes, new FakeServiceInventorySource(_ =>
        {
            _reads++;
            return Task.FromResult<IReadOnlyList<ServiceInfo>>([]);
        }), new FakeConfiguration());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            resolver.ResolveAsync([Flow(5, 1024)], new HashSet<int>(), TimeSpan.FromSeconds(seconds), Ct));

        Assert.Equal(0, _reads);
        Assert.Equal(0, _processes.ListCalls);
    }

    [Fact]
    public async Task Game_processes_are_excluded()
    {
        _processes.Start("cs2.exe", 4242);
        _processes.Start("other.exe", 5);

        var result = await Resolve([Flow(4242, 999999), Flow(5, 1024)], new HashSet<int> { 4242 });

        Assert.Equal(5, Assert.Single(result).ProcessId);
    }

    [Fact]
    public async Task Unknown_process_stays_unclassified()
    {
        _processes.Start("mystery.exe", 5);

        var result = await Resolve([Flow(5, 1024), Flow(6, 2048)]);

        Assert.All(result, c => Assert.Null(c.Kind));
        Assert.Equal("PID 6", result[0].Name);
        Assert.Equal("mystery.exe", result[1].Name);
        Assert.Empty(result[0].Services);
    }

    [Fact]
    public async Task Bytes_are_summed_per_pid_sorted_and_limited()
    {
        var config = new FakeConfiguration(diagnostics: FakeConfiguration.DefaultDiagnostics with { TopProcessCount = 3 });
        var flows = new List<TrafficFlow> { Flow(1, 1024), Flow(1, 1024) };
        for (var pid = 2; pid <= 6; pid++)
            flows.Add(Flow(pid, pid * 1000L));

        var result = await Resolve(flows, config: config);

        Assert.Equal([6, 5, 4], result.Select(c => c.ProcessId));
    }
}
