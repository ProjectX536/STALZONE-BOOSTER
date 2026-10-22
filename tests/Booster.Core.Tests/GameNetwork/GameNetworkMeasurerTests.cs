using System.Net;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.GameNetwork;
using Booster.Core.Profiles;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static Booster.Core.Tests.TestSupport.FakeNetworkAdapters;

namespace Booster.Core.Tests.GameNetwork;

public class GameNetworkMeasurerTests
{
    private const string Server = "203.0.113.5";
    private const int Rounds = 10;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeTrafficSource _traffic = new();
    private readonly FakeTcpConnections _tcp = new();
    private readonly FakeTraceRoute _trace = new();
    private readonly FakeRouteSource _route = new();
    private readonly FakeWifiSource _wifi = new();
    private readonly FakeSelfCpu _cpu = new();
    private readonly FakeActiveSessionActions _actions = new();
    private readonly InMemoryGameNetworkStore _store = new();
    private readonly FakePing _ping = new();
    private readonly FakeNetworkAdapters _adapters = new();
    private readonly FakeProcessProvider _processes = new();
    private readonly RecordingProgress<GameNetworkProgress> _progress = new();
    private FakeServiceInventorySource _services = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class ActionProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class ExitingTrace(Action onProbe) : ITraceRouteSource
    {
        public Task<HopReply> ProbeAsync(IPAddress target, int ttl, TimeSpan timeout, CancellationToken ct)
        {
            onProbe();
            return Task.FromResult(new HopReply(HopStatus.Timeout, null, null));
        }
    }

    private sealed class ThrowingTrace : ITraceRouteSource
    {
        public Task<HopReply> ProbeAsync(IPAddress target, int ttl, TimeSpan timeout, CancellationToken ct) =>
            throw new InvalidOperationException("trace failed");
    }

    private sealed class ThrowingStore : IGameNetworkStore
    {
        public Task SaveAsync(GameNetworkMeasurement measurement, CancellationToken ct) => throw new InvalidOperationException("save failed");

        public Task<IReadOnlyList<GameNetworkMeasurement>> LoadAllAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task DeleteAsync(string id, CancellationToken ct) => throw new NotSupportedException();
    }

    private GameNetworkMeasurer Measurer(ITraceRouteSource? trace = null, IGameNetworkStore? store = null)
    {
        var config = new FakeConfiguration(network: new NetworkSettings(30, 1000, 1000, null),
            gameNetwork: new GameNetworkSettings(Rounds, 10, 30, 3, 5, 20, 30, 4096));
        var resolver = new ServerResolver(_traffic, _tcp, config, _time);
        return new GameNetworkMeasurer(_processes, _adapters, _route, _wifi, _cpu, _actions, store ?? _store, resolver,
            new TraceRouteRunner(trace ?? _trace, config), new PingRoundRunner(_ping, _time),
            new ConsumerResolver(_processes, _services, config), new FakeProfileRepository(ProfileFactory.Profile(games: ["game.exe"])),
            config, _time, NullLogger<GameNetworkMeasurer>.Instance);
    }

    private async Task<GameNetworkRunResult> DriveAsync(Task<GameNetworkRunResult> run)
    {
        for (var i = 0; i < 20000 && !run.IsCompleted; i++)
        {
            await Task.Delay(1, Ct);
            _time.Advance(TimeSpan.FromSeconds(1));
        }
        return await run.WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    private Task<GameNetworkRunResult> RunAsync(string? manual = null, string? label = null, IProgress<GameNetworkProgress>? progress = null,
        GameNetworkMeasurer? measurer = null, CancellationToken? ct = null) =>
        DriveAsync((measurer ?? Measurer()).MeasureAsync(new GameNetworkRequest(manual, label), progress, ct ?? Ct));

    private void StartGameWithTcpServer(string address = Server)
    {
        _processes.Start("game.exe", 100);
        _traffic.Result = Reading<ITrafficSession>.NeedsAdmin("ETW");
        _tcp.Result = Reading<IReadOnlyList<TcpConnection>>.Known([new TcpConnection(100, IPAddress.Parse(address), 27015, true)], "fake");
    }

    private void UseAdapter(NetworkAdapterInfo adapter)
    {
        _adapters.Adapters.Add(adapter);
        _route.Result = Reading<string>.Known(adapter.Id, "fake");
    }

    [Theory]
    [InlineData("192.168.1.5")]
    [InlineData("game.example")]
    [InlineData("203.0.113")]
    public async Task Invalid_manual_server_is_rejected(string manual)
    {
        var result = await RunAsync(manual);

        Assert.Equal(GameNetworkRunStatus.InvalidManualServer, result.Status);
        Assert.Equal(0, _traffic.Starts);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public async Task No_game_and_no_manual_server_returns_no_game()
    {
        var result = await RunAsync();

        Assert.Equal(GameNetworkRunStatus.NoGame, result.Status);
        Assert.Null(result.Measurement);
        Assert.Equal(0, _traffic.Starts);
    }

    [Fact]
    public async Task Manual_server_skips_detection_and_is_saved()
    {
        var result = await RunAsync(Server);

        Assert.Equal(GameNetworkRunStatus.Saved, result.Status);
        Assert.Equal(ServerSource.Manual, result.Measurement!.Server.Source);
        Assert.Single(_store.Saved);
        Assert.Same(_store.Saved[0], result.Measurement);
        Assert.NotNull(result.Analysis);
        Assert.True(_traffic.Session.Disposed);
    }

    [Fact]
    public async Task Etw_picks_busiest_game_flow()
    {
        _processes.Start("game.exe", 100);
        _traffic.Session.Flows =
        [
            new TrafficFlow(100, IPAddress.Parse("203.0.113.10"), 500, 40_000),
            new TrafficFlow(100, IPAddress.Parse(Server), 640, 90_000),
            new TrafficFlow(200, IPAddress.Parse("198.51.100.20"), 50, 2_048_000),
        ];

        var result = await RunAsync();

        var server = result.Measurement!.Server;
        Assert.Equal(ServerSource.Etw, server.Source);
        Assert.Equal(Server, server.Address);
        Assert.Equal(2, server.Candidates.Count);
        Assert.True(result.Measurement.Consumers.IsAvailable);
        Assert.Equal(200, Assert.Single(result.Measurement.Consumers.Value!).ProcessId);
        Assert.Equal(Server, result.Measurement.Series[^1].Address);
        Assert.Equal(NetworkTargetKind.Host, result.Measurement.Series[^1].Kind);
    }

    [Fact]
    public async Task Without_admin_falls_back_to_tcp_table_and_consumers_unknown()
    {
        StartGameWithTcpServer();

        var result = await RunAsync();

        Assert.Equal(ServerSource.TcpTable, result.Measurement!.Server.Source);
        Assert.Equal(DataStatus.RequiresAdmin, result.Measurement.Consumers.Status);
    }

    [Fact]
    public async Task Failed_etw_gives_unknown_consumers()
    {
        StartGameWithTcpServer();
        _traffic.Result = Reading<ITrafficSession>.Unknown("ETW");

        var result = await RunAsync();

        Assert.Equal(DataStatus.Unknown, result.Measurement!.Consumers.Status);
    }

    [Fact]
    public async Task No_server_anywhere_returns_no_server()
    {
        _processes.Start("game.exe", 100);

        var result = await RunAsync();

        Assert.Equal(GameNetworkRunStatus.NoServer, result.Status);
        Assert.True(_traffic.Session.Disposed);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public async Task Silent_server_pings_last_responding_hop()
    {
        StartGameWithTcpServer();
        for (var ttl = 1; ttl <= 6; ttl++)
            _trace.Hops[ttl] = new HopReply(HopStatus.TtlExpired, IPAddress.Parse("10.0.0." + ttl), ttl);
        _trace.Hops[7] = new HopReply(HopStatus.TtlExpired, IPAddress.Parse("198.51.100.7"), 20);

        var result = await RunAsync();

        var measurement = result.Measurement!;
        Assert.False(measurement.Route!.ReachedTarget);
        Assert.DoesNotContain(measurement.Series, s => s.Kind == NetworkTargetKind.Host);
        var end = Assert.Single(measurement.Series, s => s.Kind == NetworkTargetKind.LastHop);
        Assert.Equal("198.51.100.7", end.Address);
        Assert.DoesNotContain(measurement.Series, s => s.Kind == NetworkTargetKind.IspHop);
    }

    [Fact]
    public async Task Isp_hop_before_end_is_pinged_separately()
    {
        StartGameWithTcpServer();
        _trace.Hops[1] = new HopReply(HopStatus.TtlExpired, IPAddress.Parse("10.0.0.1"), 1);
        _trace.Hops[2] = new HopReply(HopStatus.TtlExpired, IPAddress.Parse("198.51.100.2"), 8);
        _trace.Hops[3] = new HopReply(HopStatus.Destination, IPAddress.Parse(Server), 20);

        var result = await RunAsync();

        var kinds = result.Measurement!.Series.Select(s => (s.Kind, s.Address)).ToArray();
        Assert.Equal([(NetworkTargetKind.IspHop, "198.51.100.2"), (NetworkTargetKind.Host, Server)], kinds);
    }

    [Fact]
    public async Task Nothing_answers_gives_unknown_not_loss()
    {
        StartGameWithTcpServer();
        _ping.Replies[Server] = _ => null;

        var result = await RunAsync();

        var end = result.Analysis!.Targets[^1].Summary;
        Assert.Equal(DataStatus.Unknown, end.MedianMs.Status);
        Assert.Equal(DataStatus.Unknown, end.LossPercent.Status);
    }

    [Fact]
    public async Task Game_exit_stops_measurement_as_game_exited()
    {
        StartGameWithTcpServer();
        var progress = new ActionProgress<GameNetworkProgress>(p =>
        {
            if (p is { Phase: GameNetworkPhase.Measuring, Done: 5 })
                _processes.Exit(100);
        });

        var result = await RunAsync(progress: progress);

        Assert.Equal(MeasurementEnd.GameExited, result.Measurement!.End);
        Assert.Equal(5, result.Measurement.Series[^1].SamplesMs.Count);
    }

    [Fact]
    public async Task Reused_pid_counts_as_game_exit()
    {
        StartGameWithTcpServer();
        var progress = new ActionProgress<GameNetworkProgress>(p =>
        {
            if (p is { Phase: GameNetworkPhase.Measuring, Done: 3 })
                _processes.Start("other.exe", 100, _time.GetUtcNow().AddMinutes(5));
        });

        var result = await RunAsync(progress: progress);

        Assert.Equal(MeasurementEnd.GameExited, result.Measurement!.End);
        Assert.Equal(3, result.Measurement.Series[^1].SamplesMs.Count);
    }

    [Fact]
    public async Task Full_run_completes()
    {
        StartGameWithTcpServer();

        var result = await RunAsync();

        Assert.Equal(MeasurementEnd.Completed, result.Measurement!.End);
        Assert.Equal(Rounds, result.Measurement.Series[^1].SamplesMs.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), result.Measurement.Interval);
    }

    [Fact]
    public async Task Traffic_session_is_disposed_on_cancel_and_on_error()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var progress = new ActionProgress<GameNetworkProgress>(p =>
        {
            if (p is { Phase: GameNetworkPhase.Measuring, Done: 2 })
                cts.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(Server, progress: progress, ct: cts.Token));

        Assert.True(_traffic.Session.Disposed);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public async Task Trace_exception_degrades_to_no_route_and_session_is_disposed()
    {
        StartGameWithTcpServer();
        _traffic.Result = Reading<ITrafficSession>.Known(_traffic.Session, "fake");

        var result = await RunAsync(measurer: Measurer(trace: new ThrowingTrace()));

        Assert.Equal(GameNetworkRunStatus.Saved, result.Status);
        Assert.Null(result.Measurement!.Route);
        Assert.Equal(NetworkTargetKind.Host, result.Measurement.Series[^1].Kind);
        Assert.True(_traffic.Session.Disposed);
    }

    [Fact]
    public async Task Save_error_propagates_and_session_is_disposed()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(Server, measurer: Measurer(store: new ThrowingStore())));

        Assert.True(_traffic.Session.Disposed);
    }

    [Fact]
    public async Task Ipv6_server_uses_ipv6_gateway_or_none()
    {
        UseAdapter(Adapter("eth0", gateways: ["192.168.1.1", "fe80::1"]));

        var withGateway = await RunAsync("2001:db8::5");

        var gateway = Assert.Single(withGateway.Measurement!.Series, s => s.Kind == NetworkTargetKind.Gateway);
        Assert.Equal("fe80::1", gateway.Address);

        _adapters.Adapters.Clear();
        UseAdapter(Adapter("eth0", gateways: ["192.168.1.1"]));

        var without = await RunAsync("2001:db8::5");

        Assert.DoesNotContain(without.Measurement!.Series, s => s.Kind == NetworkTargetKind.Gateway);
    }

    [Fact]
    public async Task Ipv4_server_uses_first_ipv4_gateway()
    {
        UseAdapter(Adapter("eth0", gateways: ["fe80::1", "192.168.1.1", "not an address"]));

        var result = await RunAsync(Server);

        Assert.Equal("192.168.1.1", Assert.Single(result.Measurement!.Series, s => s.Kind == NetworkTargetKind.Gateway).Address);
        Assert.Equal("eth0", result.Measurement.Adapter!.Id);
    }

    [Fact]
    public async Task Tunnel_adapter_marks_via_tunnel()
    {
        UseAdapter(Adapter("vpn", NetworkMedium.Tunnel, gateways: ["10.8.0.1"]));

        var result = await RunAsync(Server);

        Assert.True(result.Measurement!.ViaTunnel);
    }

    [Fact]
    public async Task Unknown_adapter_gives_no_gateway_and_unsupported_wifi()
    {
        _adapters.Adapters.Add(Adapter("eth0", gateways: ["192.168.1.1"]));

        var result = await RunAsync(Server);

        var measurement = result.Measurement!;
        Assert.Null(measurement.Adapter);
        Assert.False(measurement.ViaTunnel);
        Assert.Equal(DataStatus.NotSupported, measurement.Wifi.Status);
        Assert.DoesNotContain(measurement.Series, s => s.Kind == NetworkTargetKind.Gateway);
    }

    [Fact]
    public async Task Wireless_adapter_reads_wifi_and_wired_does_not()
    {
        var status = new WifiStatus(Reading<int>.Known(-60, "fake"), Reading<int>.Unknown(), Reading<WifiBand>.Unknown(),
            Reading<bool>.Unknown(), Reading<bool>.Unknown());
        _wifi.Result = Reading<WifiStatus>.Known(status, "fake");
        UseAdapter(Adapter("wifi", NetworkMedium.Wireless, gateways: ["192.168.1.1"]));

        var wireless = await RunAsync(Server);

        Assert.Equal(-60, wireless.Measurement!.Wifi.Value!.RssiDbm.Value);

        _adapters.Adapters.Clear();
        UseAdapter(Adapter("eth0", gateways: ["192.168.1.1"]));

        var wired = await RunAsync(Server);

        Assert.Equal(DataStatus.NotSupported, wired.Measurement!.Wifi.Status);
    }

    [Fact]
    public async Task Active_actions_and_booster_cpu_are_recorded()
    {
        _actions.Actions.Add(ActionIds.PowerPlan);
        _cpu.Values.Enqueue(99);
        _cpu.Values.Enqueue(7.5);

        var result = await RunAsync(Server);

        Assert.Equal([ActionIds.PowerPlan], result.Measurement!.ActiveActions);
        Assert.Equal(7.5, result.Measurement.BoosterCpuPercent.Value);
    }

    [Fact]
    public async Task Consumer_resolver_failure_degrades_to_failure_reading_and_run_is_saved()
    {
        _processes.Start("game.exe", 100);
        _traffic.Session.Flows = [new TrafficFlow(100, IPAddress.Parse(Server), 640, 90_000), new TrafficFlow(200, IPAddress.Parse("198.51.100.20"), 5, 5000)];
        _services = new FakeServiceInventorySource(_ => throw new InvalidOperationException("services failed"));

        var result = await RunAsync();

        Assert.Equal(GameNetworkRunStatus.Saved, result.Status);
        Assert.Equal(DataStatus.Failed, result.Measurement!.Consumers.Status);
        Assert.Equal(nameof(InvalidOperationException), result.Measurement.Consumers.Note);
    }

    [Fact]
    public async Task Id_and_label_follow_the_rules()
    {
        var manual = await RunAsync(Server, "  evening run ");

        Assert.Equal("20261020-120000-manual", manual.Measurement!.Id);
        Assert.Equal("evening run", manual.Measurement.Label);
        Assert.Equal(GameNetworkMeasurement.CurrentSchemaVersion, manual.Measurement.SchemaVersion);
        Assert.Null(manual.Measurement.Game);

        StartGameWithTcpServer();
        var game = await RunAsync(label: "   ");

        Assert.Equal("game.exe", game.Measurement!.Game);
        Assert.EndsWith("-game.exe", game.Measurement.Id);
        Assert.Null(game.Measurement.Label);
    }

    [Fact]
    public async Task Progress_reports_phases_in_order()
    {
        StartGameWithTcpServer();

        await RunAsync(progress: _progress);

        var items = _progress.Items;
        Assert.Equal([GameNetworkPhase.FindingGame, GameNetworkPhase.DetectingServer, GameNetworkPhase.Tracing, GameNetworkPhase.Measuring,
            GameNetworkPhase.Saving], items.Select(i => i.Phase).Distinct());
        Assert.All(items.Where(i => i.Phase != GameNetworkPhase.Measuring), i => Assert.Equal((0, 0), (i.Done, i.Total)));
        var measuring = items.Where(i => i.Phase == GameNetworkPhase.Measuring).ToArray();
        Assert.Equal(Enumerable.Range(0, Rounds + 1), measuring.Select(i => i.Done));
        Assert.All(measuring, i => Assert.Equal(Rounds, i.Total));
        Assert.Equal(GameNetworkPhase.Saving, items[^1].Phase);
    }

    [Fact]
    public async Task Game_exit_during_tracing_is_recorded_as_game_exited_without_samples()
    {
        StartGameWithTcpServer();

        var result = await RunAsync(measurer: Measurer(trace: new ExitingTrace(() => _processes.Exit(100))));

        Assert.Equal(MeasurementEnd.GameExited, result.Measurement!.End);
        Assert.Empty(result.Measurement.Series[^1].SamplesMs);
        Assert.Equal(DataStatus.Unknown, result.Analysis!.Targets[^1].Summary.MedianMs.Status);
    }

    [Fact]
    public async Task Unreadable_identity_and_pid_gone_from_list_stops_measurement()
    {
        StartGameWithTcpServer();
        _processes.DenyRead(100);
        var progress = new ActionProgress<GameNetworkProgress>(p =>
        {
            if (p is { Phase: GameNetworkPhase.Measuring, Done: 3 })
                _processes.Exit(100);
        });

        var result = await RunAsync(progress: progress);

        Assert.Equal(MeasurementEnd.GameExited, result.Measurement!.End);
        Assert.Equal(3, result.Measurement.Series[^1].SamplesMs.Count);
    }

    [Fact]
    public async Task Unreadable_identity_and_pid_still_listed_keeps_going()
    {
        StartGameWithTcpServer();
        _processes.DenyRead(100);

        var result = await RunAsync();

        Assert.Equal(MeasurementEnd.Completed, result.Measurement!.End);
        Assert.Equal(Rounds, result.Measurement.Series[^1].SamplesMs.Count);
    }

    [Fact]
    public async Task Tunnel_keyword_in_adapter_name_marks_via_tunnel()
    {
        UseAdapter(Adapter("wireguard0", gateways: ["10.8.0.1"]));

        var result = await RunAsync(Server);

        Assert.True(result.Measurement!.ViaTunnel);
    }

    [Fact]
    public async Task Second_run_while_one_is_running_is_rejected_and_guard_is_released()
    {
        var measurer = Measurer();
        var first = measurer.MeasureAsync(new GameNetworkRequest(Server, null), null, Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => measurer.MeasureAsync(new GameNetworkRequest(Server, null), null, Ct));
        Assert.Equal(GameNetworkRunStatus.Saved, (await DriveAsync(first)).Status);

        var third = await RunAsync(Server, measurer: measurer);

        Assert.Equal(GameNetworkRunStatus.Saved, third.Status);
    }
}
