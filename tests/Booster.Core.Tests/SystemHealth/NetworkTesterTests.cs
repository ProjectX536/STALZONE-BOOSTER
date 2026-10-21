using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;
using static Booster.Core.Tests.TestSupport.FakeNetworkAdapters;

namespace Booster.Core.Tests.SystemHealth;

public class NetworkTesterTests
{
    private const string Gateway = "192.168.1.1";
    private const string Host = "1.1.1.1";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly FakePing _ping = new();
    private readonly FakeNetworkAdapters _adapters = new();
    private readonly RecordingProgress<int> _progress = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private NetworkTester Tester(string? host = Host, int count = 3) =>
        new(_ping, _adapters, new FakeConfiguration(network: new NetworkSettings(count, (int)Interval.TotalMilliseconds, 1000, host)), _time);

    /// <summary>Двигает время на интервал после каждого завершённого раунда: пауза уже ждёт таймер.</summary>
    private async Task<NetworkTestResult> DriveAsync(Task<NetworkTestResult> run)
    {
        var advanced = 0;
        for (var i = 0; i < 5000 && !run.IsCompleted; i++)
        {
            if (_progress.Items.Count > advanced)
            {
                await Task.Delay(5, Ct);
                _time.Advance(Interval);
                advanced++;
            }
            else
            {
                await Task.Delay(1, Ct);
            }
        }
        return await run.WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    [Fact]
    public async Task Pings_gateway_and_configured_host_each_round()
    {
        _adapters.Adapters.Add(Adapter("wifi", NetworkMedium.Wireless, gateways: []));
        _adapters.Adapters.Add(Adapter("eth0", gateways: [Gateway, "10.0.0.1"]));
        _ping.Replies[Host] = attempt => attempt == 1 ? null : 30;

        var result = await DriveAsync(Tester().RunAsync(_progress, Ct));

        Assert.Equal("eth0", result.Adapter!.Id);
        Assert.Equal(3, _ping.Calls.Count(c => c == Gateway));
        Assert.Equal(3, _ping.Calls.Count(c => c == Host));
        Assert.DoesNotContain("10.0.0.1", _ping.Calls);
        Assert.Equal(TimeSpan.FromSeconds(1), _ping.LastTimeout);
        Assert.Equal([NetworkTargetKind.Gateway, NetworkTargetKind.Host], result.Targets.Select(t => t.Kind));
        Assert.Equal([Gateway, Host], result.Targets.Select(t => t.Address));
        Assert.Equal((3, 3), (result.Targets[0].Sent, result.Targets[0].Received));
        Assert.Equal((3, 2), (result.Targets[1].Sent, result.Targets[1].Received));
        Assert.Equal(_time.Start, result.StartedAt);
        Assert.Equal(TimeSpan.FromSeconds(30), result.Duration);
    }

    [Fact]
    public async Task Unresolvable_host_fails_only_its_target()
    {
        _adapters.Adapters.Add(Adapter(gateways: Gateway));
        _ping.Unresolvable.Add("no.such.host");

        var result = await DriveAsync(Tester("no.such.host").RunAsync(_progress, Ct));

        Assert.Equal(1, _ping.Calls.Count(c => c == "no.such.host"));
        Assert.Equal(3, _ping.Calls.Count(c => c == Gateway));
        var host = result.Targets[1];
        Assert.Equal(DataStatus.Failed, host.MedianMs.Status);
        Assert.Equal(DataStatus.Failed, host.JitterMs.Status);
        Assert.Equal(DataStatus.Failed, host.LossPercent.Status);
        Assert.Contains("No such host is known", host.MedianMs.Note, StringComparison.Ordinal);
        Assert.Equal(10, result.Targets[0].MedianMs.Value);
    }

    [Fact]
    public async Task No_gateway_and_no_host_gives_empty_targets()
    {
        _adapters.Adapters.Add(Adapter(gateways: []));

        var result = await DriveAsync(Tester(host: null).RunAsync(_progress, Ct));

        Assert.Null(result.Adapter);
        Assert.Empty(result.Targets);
        Assert.Empty(_ping.Calls);
        Assert.Empty(_progress.Items);
    }

    [Fact]
    public async Task Utilization_from_traffic_delta()
    {
        _adapters.Adapters.Add(Adapter(speed: 100_000_000, gateways: Gateway));
        _adapters.Traffic.Enqueue(Reading<long>.Known(5_000_000, "fake"));
        _adapters.Traffic.Enqueue(Reading<long>.Known(6_000_000, "fake"));

        var result = await DriveAsync(Tester().RunAsync(_progress, Ct));

        Assert.Equal(0.27, result.AdapterUtilizationPercent.Value, 2);
    }

    [Fact]
    public async Task Utilization_without_speed_or_traffic_is_unknown()
    {
        _adapters.Adapters.Add(Adapter(speed: null, gateways: Gateway));
        _adapters.Traffic.Enqueue(Reading<long>.Known(5_000_000, "fake"));
        _adapters.Traffic.Enqueue(Reading<long>.Known(6_000_000, "fake"));

        var withoutSpeed = await DriveAsync(Tester().RunAsync(_progress, Ct));

        Assert.False(withoutSpeed.AdapterUtilizationPercent.IsAvailable);
    }

    [Fact]
    public async Task Cancellation_throws()
    {
        _adapters.Adapters.Add(Adapter(gateways: Gateway));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var run = Tester().RunAsync(_progress, cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(10), Ct));
    }

    [Fact]
    public async Task Progress_reports_rounds()
    {
        _adapters.Adapters.Add(Adapter(gateways: Gateway));

        await DriveAsync(Tester(count: 4).RunAsync(_progress, Ct));

        Assert.Equal([1, 2, 3, 4], _progress.Items);
    }
}
