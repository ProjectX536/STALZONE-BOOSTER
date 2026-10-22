using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.GameNetwork;
using Booster.Core.SystemHealth;

namespace Booster.Core.Tests.TestSupport;

/// <summary>Замер сети в игре: шлюз 2 мс, провайдер 12 мс, сервер 40 мс и два скачка за роутером.</summary>
public static class GameNetworkSamples
{
    public const string Server = "203.0.113.5";
    public const string Gateway = "192.168.1.1";
    public const string IspHop = "100.70.1.1";
    public const string MiddleHop = "198.51.100.7";
    public const int Rounds = 30;

    public static readonly DateTimeOffset StartedAt = new(2026, 10, 21, 20, 0, 0, TimeSpan.FromHours(3));

    private const string Source = "test";

    public static GameNetworkMeasurement Measurement(string id = "20261021-200000", string? label = null,
        IReadOnlyList<string>? actions = null, MeasurementEnd end = MeasurementEnd.Completed, NetworkAdapterInfo? adapter = null,
        Reading<WifiStatus>? wifi = null, Reading<IReadOnlyList<ConsumerTraffic>>? consumers = null) => new(
        GameNetworkMeasurement.CurrentSchemaVersion,
        id,
        StartedAt,
        TimeSpan.FromSeconds(1),
        "stalker2",
        label,
        new MeasuredServer(Server, ServerSource.Etw, [new ServerCandidate(Server, 60)]),
        adapter ?? new NetworkAdapterInfo("{wifi}", "Wi-Fi", "Intel AX200", NetworkMedium.Wireless, 866_000_000, [Gateway]),
        false,
        [
            new TargetSeries(NetworkTargetKind.Gateway, Gateway, Flat(2)),
            new TargetSeries(NetworkTargetKind.IspHop, IspHop, Flat(12)),
            new TargetSeries(NetworkTargetKind.Host, Server, Flat(40, spikes: [5, 20])),
        ],
        new RouteTrace(Server,
        [
            new RouteHop(1, Gateway, AddressScope.Private, [1.0, 1.0, 1.0]),
            new RouteHop(2, IspHop, AddressScope.Cgnat, [11.0, 12.0, 13.0]),
            new RouteHop(3, MiddleHop, AddressScope.Public, [35.0, 36.0, 37.0]),
            new RouteHop(4, Server, AddressScope.Public, [40.0, 41.0, 39.0]),
        ], true),
        wifi ?? Reading<WifiStatus>.Known(new WifiStatus(
            Reading<int>.Known(-61, Source),
            Reading<int>.Known(36, Source),
            Reading<WifiBand>.Known(WifiBand.Band5GHz, Source),
            Reading<bool>.Known(true, Source),
            Reading<bool>.Known(false, Source)), Source),
        consumers ?? Reading<IReadOnlyList<ConsumerTraffic>>.Known(
            [new ConsumerTraffic(4242, "steam", [], NetworkConsumerKind.Downloader, 350.5)], Source),
        actions ?? [],
        Reading<double>.Known(0.8, Source),
        end);

    public static GameNetworkAnalysis Analysis(GameNetworkMeasurement measurement) =>
        GameNetworkAnalyzer.Analyze(measurement, FakeConfiguration.DefaultGameNetworkThresholds, FakeConfiguration.DefaultThresholds.Network);

    public static LoadTestResult Load() => new(StartedAt.AddMinutes(5), NetworkTargetKind.Host, Reading<double>.Known(40, Source),
        Reading<double>.Known(120, Source), Reading<double>.Known(50, Source), BufferbloatGrade.D, LoadTestOutcome.Graded);

    public static GameNetworkComparison Comparison() => new(Server, 3, 3,
        [new NetworkMetricComparison(NetworkMetric.MedianMs, 40, 38, -2, ChangeVerdict.NoChange)],
        [ComparisonCaveat.HighRunVariation], ChangeVerdict.NoChange);

    private static double?[] Flat(double ms, int[]? spikes = null) =>
        Enumerable.Range(0, Rounds).Select(i => (double?)(spikes is not null && spikes.Contains(i) ? ms + 100 : ms)).ToArray();
}
