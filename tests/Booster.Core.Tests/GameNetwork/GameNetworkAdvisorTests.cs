using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.GameNetwork;
using Booster.Core.Reports;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.GameNetwork;

public class GameNetworkAdvisorTests
{
    private static readonly GameNetworkThresholds Thresholds = FakeConfiguration.DefaultGameNetworkThresholds;
    private static readonly NetworkThresholds Network = new(10, 100, 10, 2);

    private static readonly NetworkAdapterInfo Wired = new("a", "Ethernet", "NIC", NetworkMedium.Ethernet, 1_000_000_000, ["192.168.1.1"]);
    private static readonly NetworkAdapterInfo Wireless = Wired with { Medium = NetworkMedium.Wireless };

    private static GameNetworkMeasurement Measurement(NetworkAdapterInfo? adapter = null, bool viaTunnel = false,
        Reading<WifiStatus>? wifi = null, Reading<IReadOnlyList<ConsumerTraffic>>? consumers = null) => new(
        GameNetworkMeasurement.CurrentSchemaVersion, "id", DateTimeOffset.UnixEpoch, TimeSpan.FromMilliseconds(500), null, null,
        new MeasuredServer("203.0.113.7", ServerSource.Etw, []), adapter ?? Wired, viaTunnel,
        [new TargetSeries(NetworkTargetKind.Host, "203.0.113.7", [30.0])], null,
        wifi ?? Reading<WifiStatus>.Unknown("wlan"),
        consumers ?? Reading<IReadOnlyList<ConsumerTraffic>>.Unknown("etw"), [], Reading<double>.Unknown("self"),
        MeasurementEnd.Completed);

    private static Reading<double> Known(double value) => Reading<double>.Known(value, "test");

    private static LatencyBreakdown Breakdown(double? total = 30, NetworkTargetKind endKind = NetworkTargetKind.Host) =>
        new(Reading<double>.Unknown("test"), Reading<double>.Unknown("test"), Reading<double>.Unknown("test"),
            total is null ? Reading<double>.Unknown("test") : Known(total.Value), endKind, false);

    private static SpikeReport Spikes(int endCount = 0, int atHome = 0, int beyond = 0, double? gatewayPeriod = null,
        bool hasGateway = true) => new(
        hasGateway ? new SeriesSpikes(endCount, [], gatewayPeriod is null ? Reading<double>.Unknown("test") : Known(gatewayPeriod.Value)) : null,
        new SeriesSpikes(endCount, [], Reading<double>.Unknown("test")), atHome, beyond);

    private static WifiStatus Wifi(int? rssi = null, int? channel = null, WifiBand? band = null) => new(
        rssi is null ? Reading<int>.Unknown("wlan") : Reading<int>.Known(rssi.Value, "wlan"),
        channel is null ? Reading<int>.Unknown("wlan") : Reading<int>.Known(channel.Value, "wlan"),
        band is null ? Reading<WifiBand>.Unknown("wlan") : Reading<WifiBand>.Known(band.Value, "wlan"),
        Reading<bool>.Unknown("wlan"), Reading<bool>.Unknown("wlan"));

    private static Reading<IReadOnlyList<ConsumerTraffic>> Consumers(params ConsumerTraffic[] consumers) =>
        Reading<IReadOnlyList<ConsumerTraffic>>.Known(consumers, "etw");

    private static IReadOnlyList<SystemAdvice> Advise(GameNetworkMeasurement m, LatencyBreakdown? breakdown = null,
        SpikeReport? spikes = null, RouteJump? jump = null) =>
        GameNetworkAdvisor.Advise(m, breakdown ?? Breakdown(), spikes ?? Spikes(), jump, Thresholds, Network);

    private static SystemAdvice? Find(IEnumerable<SystemAdvice> advice, AdviceId id) => advice.SingleOrDefault(a => a.Id == id);

    private static LoadTestResult Load(LoadTestOutcome outcome, BufferbloatGrade? grade, double idle = 20, double loaded = 95.5) => new(
        DateTimeOffset.UnixEpoch, NetworkTargetKind.Host, Known(idle), Known(loaded), Known(50), grade, outcome);

    [Theory]
    [InlineData(BufferbloatGrade.C)]
    [InlineData(BufferbloatGrade.D)]
    [InlineData(BufferbloatGrade.F)]
    public void Bufferbloat_advice_from_grade_c(BufferbloatGrade grade)
    {
        var advice = GameNetworkAdvisor.AdviseLoad(Load(LoadTestOutcome.Graded, grade));

        var found = Assert.Single(advice);
        Assert.Equal(AdviceId.GameNetworkBufferbloat, found.Id);
        Assert.Equal(AdviceModule.GameNetwork, found.Module);
        Assert.Equal(SafetyLevel.L3ManualOnly, found.Level);
        Assert.Equal(FindingSeverity.Warning, found.Severity);
        Assert.Equal(["76"], found.Arguments);
    }

    [Theory]
    [InlineData(BufferbloatGrade.APlus)]
    [InlineData(BufferbloatGrade.A)]
    [InlineData(BufferbloatGrade.B)]
    public void Good_grade_has_no_bufferbloat_advice(BufferbloatGrade grade) =>
        Assert.Empty(GameNetworkAdvisor.AdviseLoad(Load(LoadTestOutcome.Graded, grade)));

    [Fact]
    public void No_load_has_no_bufferbloat_advice() =>
        Assert.Empty(GameNetworkAdvisor.AdviseLoad(Load(LoadTestOutcome.NoLoad, null)));

    [Fact]
    public void Wifi_scan_spikes_need_wireless_adapter_and_known_period()
    {
        var advice = Advise(Measurement(Wireless), spikes: Spikes(5, 5, 0, gatewayPeriod: 60.4));

        var found = Find(advice, AdviceId.GameNetworkWifiScanSpikes);
        Assert.NotNull(found);
        Assert.Equal(["60"], found.Arguments);
        Assert.Equal(FindingSeverity.Warning, found.Severity);
        Assert.Null(Find(advice, AdviceId.GameNetworkLocalSpikes));
        Assert.Null(Find(Advise(Measurement(Wireless), spikes: Spikes(5, 5, 0)), AdviceId.GameNetworkWifiScanSpikes));
    }

    [Fact]
    public void Wired_periodic_spikes_are_not_wifi_scan()
    {
        var advice = Advise(Measurement(Wired), spikes: Spikes(5, 5, 0, gatewayPeriod: 60));

        Assert.Null(Find(advice, AdviceId.GameNetworkWifiScanSpikes));
        Assert.NotNull(Find(advice, AdviceId.GameNetworkLocalSpikes));
    }

    [Fact]
    public void Local_spikes_start_at_home()
    {
        var advice = Advise(Measurement(), spikes: Spikes(4, 2, 2));

        Assert.Equal(["2", "4"], Find(advice, AdviceId.GameNetworkLocalSpikes)!.Arguments);
        Assert.Null(Find(advice, AdviceId.GameNetworkRemoteSpikes));
    }

    [Fact]
    public void Local_spikes_are_not_advised_below_the_boundary()
    {
        Assert.Null(Find(Advise(Measurement(), spikes: Spikes(5, 2, 3)), AdviceId.GameNetworkLocalSpikes));
        Assert.Null(Find(Advise(Measurement(), spikes: Spikes(2, 2, 0)), AdviceId.GameNetworkLocalSpikes));
        Assert.Null(Find(Advise(Measurement(), spikes: Spikes(4, 4, 0, hasGateway: false)), AdviceId.GameNetworkLocalSpikes));
    }

    [Fact]
    public void Remote_spikes_are_beyond_the_gateway()
    {
        var advice = Advise(Measurement(), spikes: Spikes(5, 2, 3));

        Assert.Equal(["3", "5"], Find(advice, AdviceId.GameNetworkRemoteSpikes)!.Arguments);
        Assert.Null(Find(advice, AdviceId.GameNetworkLocalSpikes));
    }

    [Fact]
    public void Remote_spikes_are_not_advised_below_the_boundary()
    {
        Assert.Null(Find(Advise(Measurement(), spikes: Spikes(4, 2, 2)), AdviceId.GameNetworkRemoteSpikes));
        Assert.Null(Find(Advise(Measurement(), spikes: Spikes(2, 0, 2)), AdviceId.GameNetworkRemoteSpikes));
        Assert.Null(Find(Advise(Measurement(), spikes: Spikes(5, 0, 5, hasGateway: false)), AdviceId.GameNetworkRemoteSpikes));
    }

    [Fact]
    public void Route_jump_is_advised_with_ttl_and_increase()
    {
        var advice = Advise(Measurement(), jump: new RouteJump(4, 5, 24.6));

        var found = Find(advice, AdviceId.GameNetworkRouteJump);
        Assert.Equal(["5", "25"], found!.Arguments);
        Assert.Equal(FindingSeverity.Info, found.Severity);
        Assert.Null(Find(Advise(Measurement()), AdviceId.GameNetworkRouteJump));
    }

    [Fact]
    public void Far_server_needs_host_end_at_or_above_the_threshold()
    {
        var found = Find(Advise(Measurement(), Breakdown(100)), AdviceId.GameNetworkFarServer);
        Assert.Equal(["100"], found!.Arguments);
        Assert.Equal(FindingSeverity.Info, found.Severity);

        Assert.Null(Find(Advise(Measurement(), Breakdown(99.9)), AdviceId.GameNetworkFarServer));
        Assert.Null(Find(Advise(Measurement(), Breakdown(null)), AdviceId.GameNetworkFarServer));
        Assert.Null(Find(Advise(Measurement(), Breakdown(150, NetworkTargetKind.LastHop)), AdviceId.GameNetworkFarServer));
    }

    [Fact]
    public void Vpn_is_advised_only_through_a_tunnel()
    {
        var found = Find(Advise(Measurement(viaTunnel: true)), AdviceId.GameNetworkVpn);

        Assert.NotNull(found);
        Assert.Empty(found.Arguments);
        Assert.Equal(FindingSeverity.Warning, found.Severity);
        Assert.Null(Find(Advise(Measurement()), AdviceId.GameNetworkVpn));
    }

    [Fact]
    public void Background_traffic_is_advised_per_consumer_at_the_threshold()
    {
        var advice = Advise(Measurement(consumers: Consumers(
            new ConsumerTraffic(1, "steam", [], NetworkConsumerKind.Downloader, 100),
            new ConsumerTraffic(2, "onedrive", [], NetworkConsumerKind.CloudSync, 250.4),
            new ConsumerTraffic(3, "quiet", [], NetworkConsumerKind.Downloader, 99.9))));

        var found = advice.Where(a => a.Id == AdviceId.GameNetworkBackgroundTraffic).ToArray();
        Assert.Equal(2, found.Length);
        Assert.Equal(["steam", "100"], found[0].Arguments);
        Assert.Equal(["onedrive", "250"], found[1].Arguments);
        Assert.All(found, a => Assert.Equal(FindingSeverity.Warning, a.Severity));
    }

    [Fact]
    public void Consumer_outside_the_catalog_gets_background_traffic_advice()
    {
        var advice = Advise(Measurement(consumers: Consumers(new ConsumerTraffic(1, "mystery", [], null, 500))));

        Assert.Equal(["mystery", "500"], Find(advice, AdviceId.GameNetworkBackgroundTraffic)!.Arguments);
    }

    [Fact]
    public void Delivery_optimization_has_its_own_advice()
    {
        var advice = Advise(Measurement(consumers: Consumers(
            new ConsumerTraffic(1, "svchost", ["DoSvc"], NetworkConsumerKind.DeliveryOptimization, 300),
            new ConsumerTraffic(2, "svchost2", ["DoSvc"], NetworkConsumerKind.DeliveryOptimization, 800.2),
            new ConsumerTraffic(3, "quiet", ["DoSvc"], NetworkConsumerKind.DeliveryOptimization, 50))));

        Assert.Equal(["800"], Find(advice, AdviceId.GameNetworkDeliveryOptimization)!.Arguments);
        Assert.Null(Find(advice, AdviceId.GameNetworkBackgroundTraffic));
        Assert.Null(Find(advice, AdviceId.GameNetworkWindowsUpdate));
        Assert.Null(Find(Advise(Measurement(consumers: Consumers(
            new ConsumerTraffic(1, "svchost", [], NetworkConsumerKind.DeliveryOptimization, 99)))),
            AdviceId.GameNetworkDeliveryOptimization));
    }

    [Fact]
    public void Windows_update_has_its_own_advice()
    {
        var advice = Advise(Measurement(consumers: Consumers(
            new ConsumerTraffic(1, "wuauserv", ["wuauserv"], NetworkConsumerKind.WindowsUpdate, 100))));

        var found = Find(advice, AdviceId.GameNetworkWindowsUpdate);
        Assert.Equal(["100"], found!.Arguments);
        Assert.Equal(FindingSeverity.Warning, found.Severity);
        Assert.Null(Find(advice, AdviceId.GameNetworkBackgroundTraffic));
        Assert.Null(Find(Advise(Measurement(consumers: Consumers(
            new ConsumerTraffic(1, "wuauserv", [], NetworkConsumerKind.WindowsUpdate, 99.9)))), AdviceId.GameNetworkWindowsUpdate));
    }

    [Fact]
    public void Unknown_consumers_give_no_traffic_advice()
    {
        foreach (var consumers in new[]
                 {
                     Reading<IReadOnlyList<ConsumerTraffic>>.Unknown("etw"),
                     Reading<IReadOnlyList<ConsumerTraffic>>.NeedsAdmin("etw"),
                 })
        {
            var advice = Advise(Measurement(consumers: consumers));

            Assert.DoesNotContain(advice, a => a.Id is AdviceId.GameNetworkBackgroundTraffic
                or AdviceId.GameNetworkDeliveryOptimization or AdviceId.GameNetworkWindowsUpdate);
        }
    }

    [Fact]
    public void Wifi_on_2_4_ghz_needs_a_known_band_and_channel()
    {
        var advice = Advise(Measurement(Wireless, wifi: Reading<WifiStatus>.Known(Wifi(channel: 6, band: WifiBand.Band24GHz), "wlan")));

        var found = Find(advice, AdviceId.GameNetworkWifi24GHz);
        Assert.Equal(["6"], found!.Arguments);
        Assert.Equal(FindingSeverity.Info, found.Severity);

        Assert.Null(Find(Advise(Measurement(Wireless, wifi: Reading<WifiStatus>.Known(Wifi(channel: 36, band: WifiBand.Band5GHz), "wlan"))),
            AdviceId.GameNetworkWifi24GHz));
        Assert.Null(Find(Advise(Measurement(Wireless, wifi: Reading<WifiStatus>.Known(Wifi(channel: 6), "wlan"))),
            AdviceId.GameNetworkWifi24GHz));
        Assert.Null(Find(Advise(Measurement(Wireless, wifi: Reading<WifiStatus>.Known(Wifi(band: WifiBand.Band24GHz), "wlan"))),
            AdviceId.GameNetworkWifi24GHz));
    }

    [Fact]
    public void Weak_wifi_signal_is_below_the_threshold()
    {
        var advice = Advise(Measurement(Wireless, wifi: Reading<WifiStatus>.Known(Wifi(rssi: -68), "wlan")));

        var found = Find(advice, AdviceId.GameNetworkWifiWeakSignal);
        Assert.Equal(["-68"], found!.Arguments);
        Assert.Equal(FindingSeverity.Warning, found.Severity);

        Assert.Null(Find(Advise(Measurement(Wireless, wifi: Reading<WifiStatus>.Known(Wifi(rssi: -67), "wlan"))),
            AdviceId.GameNetworkWifiWeakSignal));
        Assert.Null(Find(Advise(Measurement(Wireless, wifi: Reading<WifiStatus>.Known(Wifi(), "wlan"))),
            AdviceId.GameNetworkWifiWeakSignal));
    }

    [Fact]
    public void Wireless_advice_follows_spike_or_wifi_advice_on_a_wireless_adapter()
    {
        var withSpikes = Advise(Measurement(Wireless), spikes: Spikes(5, 2, 3));
        var found = Find(withSpikes, AdviceId.GameNetworkWireless);
        Assert.NotNull(found);
        Assert.Empty(found.Arguments);
        Assert.Equal(FindingSeverity.Info, found.Severity);

        var withWifi = Advise(Measurement(Wireless, wifi: Reading<WifiStatus>.Known(Wifi(rssi: -80), "wlan")));
        Assert.NotNull(Find(withWifi, AdviceId.GameNetworkWireless));

        Assert.Null(Find(Advise(Measurement(Wireless)), AdviceId.GameNetworkWireless));
        Assert.Null(Find(Advise(Measurement(Wireless), jump: new RouteJump(1, 2, 40)), AdviceId.GameNetworkWireless));
        Assert.Null(Find(Advise(Measurement(Wired), spikes: Spikes(5, 2, 3)), AdviceId.GameNetworkWireless));
    }

    [Fact]
    public void Advice_comes_in_table_order()
    {
        var advice = Advise(
            Measurement(Wireless, viaTunnel: true,
                wifi: Reading<WifiStatus>.Known(Wifi(-80, 6, WifiBand.Band24GHz), "wlan"),
                consumers: Consumers(
                    new ConsumerTraffic(1, "steam", [], NetworkConsumerKind.Downloader, 500),
                    new ConsumerTraffic(2, "wu", [], NetworkConsumerKind.WindowsUpdate, 500))),
            Breakdown(150), Spikes(5, 5, 0, gatewayPeriod: 30), new RouteJump(3, 4, 40));

        Assert.Equal(
        [
            AdviceId.GameNetworkWifiScanSpikes, AdviceId.GameNetworkRouteJump, AdviceId.GameNetworkFarServer, AdviceId.GameNetworkVpn,
            AdviceId.GameNetworkBackgroundTraffic, AdviceId.GameNetworkWindowsUpdate, AdviceId.GameNetworkWifi24GHz,
            AdviceId.GameNetworkWifiWeakSignal, AdviceId.GameNetworkWireless,
        ], advice.Select(a => a.Id).ToArray());
    }

    [Fact]
    public void All_game_network_advice_is_manual_only()
    {
        var advice = Advise(
            Measurement(Wireless, viaTunnel: true,
                wifi: Reading<WifiStatus>.Known(Wifi(-80, 6, WifiBand.Band24GHz), "wlan"),
                consumers: Consumers(new ConsumerTraffic(1, "steam", [], NetworkConsumerKind.Downloader, 500))),
            Breakdown(150), Spikes(5, 2, 3), new RouteJump(3, 4, 40));

        Assert.NotEmpty(advice);
        Assert.All(advice, a =>
        {
            Assert.Equal(SafetyLevel.L3ManualOnly, a.Level);
            Assert.Equal(AdviceModule.GameNetwork, a.Module);
        });
    }

    [Fact]
    public void Quiet_measurement_gives_no_advice()
    {
        Assert.Empty(Advise(Measurement()));
    }

    [Fact]
    public void Bufferbloat_is_not_emitted_here()
    {
        var advice = Advise(
            Measurement(Wireless, viaTunnel: true, wifi: Reading<WifiStatus>.Known(Wifi(-80, 6, WifiBand.Band24GHz), "wlan")),
            Breakdown(150), Spikes(5, 2, 3), new RouteJump(3, 4, 40));

        Assert.DoesNotContain(advice, a => a.Id == AdviceId.GameNetworkBufferbloat);
    }
}
