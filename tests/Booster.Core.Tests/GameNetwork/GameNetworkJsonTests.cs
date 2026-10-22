using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.GameNetwork;
using Booster.Core.SystemHealth;

namespace Booster.Core.Tests.GameNetwork;

public class GameNetworkJsonTests
{
    private static GameNetworkMeasurement Sample() => new(
        GameNetworkMeasurement.CurrentSchemaVersion,
        "20261021-120000",
        new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.FromHours(3)),
        TimeSpan.FromMilliseconds(500),
        "stalker2",
        "после игры",
        new MeasuredServer("203.0.113.7", ServerSource.Etw,
            [new ServerCandidate("203.0.113.7", 41.5), new ServerCandidate("198.51.100.9", null)]),
        new NetworkAdapterInfo("{guid}", "Wi-Fi", "Intel AX200", NetworkMedium.Wireless, 866_000_000, ["192.168.1.1"]),
        false,
        [
            new TargetSeries(NetworkTargetKind.Gateway, "192.168.1.1", [1.5, 2.0, null]),
            new TargetSeries(NetworkTargetKind.IspHop, "10.0.0.1", [8.0, null, 9.5]),
            new TargetSeries(NetworkTargetKind.Host, "203.0.113.7", [30.0, 31.0, 29.5]),
        ],
        new RouteTrace("203.0.113.7",
        [
            new RouteHop(1, "192.168.1.1", AddressScope.Private, [1.0, 1.2, 1.1]),
            new RouteHop(2, null, null, [null, null, null]),
            new RouteHop(3, "203.0.113.7", AddressScope.Public, [30.0, null, 31.0]),
        ], true),
        Reading<WifiStatus>.Known(new WifiStatus(
            Reading<int>.Known(-61, "wlan"),
            Reading<int>.Known(36, "wlan"),
            Reading<WifiBand>.Unknown("wlan", "неоднозначный канал"),
            Reading<bool>.Known(true, "wlan"),
            Reading<bool>.Unknown("wlan")), "wlan"),
        Reading<IReadOnlyList<ConsumerTraffic>>.NeedsAdmin("etw", "нужны права администратора"),
        ["power-plan", "process-priority"],
        Reading<double>.Known(1.25, "self"),
        MeasurementEnd.GameExited);

    [Fact]
    public void Measurement_round_trips()
    {
        var original = Sample();
        var json = GameNetworkJson.Serialize(original);

        var loaded = GameNetworkJson.Deserialize(json);

        Assert.NotNull(loaded);
        Assert.Equal(original.SchemaVersion, loaded.SchemaVersion);
        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal(original.StartedAt, loaded.StartedAt);
        Assert.Equal(original.Interval, loaded.Interval);
        Assert.Equal(original.Game, loaded.Game);
        Assert.Equal(original.Label, loaded.Label);
        Assert.Equal(original.Server.Address, loaded.Server.Address);
        Assert.Equal(original.Server.Source, loaded.Server.Source);
        Assert.Equal(original.Server.Candidates, loaded.Server.Candidates);
        Assert.Equal(original.Adapter!.Id, loaded.Adapter!.Id);
        Assert.Equal(original.Adapter.Medium, loaded.Adapter.Medium);
        Assert.Equal(original.Adapter.LinkSpeedBitsPerSecond, loaded.Adapter.LinkSpeedBitsPerSecond);
        Assert.Equal(original.Adapter.Gateways, loaded.Adapter.Gateways);
        Assert.Equal(original.ViaTunnel, loaded.ViaTunnel);

        Assert.Equal(original.Series.Count, loaded.Series.Count);
        for (var i = 0; i < original.Series.Count; i++)
        {
            Assert.Equal(original.Series[i].Kind, loaded.Series[i].Kind);
            Assert.Equal(original.Series[i].Address, loaded.Series[i].Address);
            Assert.Equal(original.Series[i].SamplesMs, loaded.Series[i].SamplesMs);
        }

        Assert.Equal(original.Route!.Target, loaded.Route!.Target);
        Assert.Equal(original.Route.ReachedTarget, loaded.Route.ReachedTarget);
        Assert.Equal(original.Route.Hops.Count, loaded.Route.Hops.Count);
        for (var i = 0; i < original.Route.Hops.Count; i++)
        {
            Assert.Equal(original.Route.Hops[i].Ttl, loaded.Route.Hops[i].Ttl);
            Assert.Equal(original.Route.Hops[i].Address, loaded.Route.Hops[i].Address);
            Assert.Equal(original.Route.Hops[i].Scope, loaded.Route.Hops[i].Scope);
            Assert.Equal(original.Route.Hops[i].ProbesMs, loaded.Route.Hops[i].ProbesMs);
        }

        Assert.Equal(original.Wifi, loaded.Wifi with { Value = original.Wifi.Value }, new ReadingStatusComparer<WifiStatus>());
        Assert.Equal(original.Wifi.Value, loaded.Wifi.Value);
        Assert.Equal(DataStatus.Unknown, loaded.Wifi.Value!.Band.Status);
        Assert.Equal(DataStatus.Available, loaded.Wifi.Value.RssiDbm.Status);
        Assert.Equal(-61, loaded.Wifi.Value.RssiDbm.Value);

        Assert.Equal(DataStatus.RequiresAdmin, loaded.Consumers.Status);
        Assert.Equal(original.Consumers.Note, loaded.Consumers.Note);
        Assert.Null(loaded.Consumers.Value);

        Assert.Equal(original.ActiveActions, loaded.ActiveActions);
        Assert.Equal(original.BoosterCpuPercent, loaded.BoosterCpuPercent);
        Assert.Equal(MeasurementEnd.GameExited, loaded.End);

        Assert.Equal(json, GameNetworkJson.Serialize(loaded));
    }

    [Fact]
    public void Available_consumers_round_trip()
    {
        var original = Sample() with
        {
            Consumers = Reading<IReadOnlyList<ConsumerTraffic>>.Known(
                [
                    new ConsumerTraffic(4242, "steam", ["SteamSvc"], NetworkConsumerKind.Downloader, 812.5),
                    new ConsumerTraffic(7, "unknown", [], null, 3.0),
                ], "etw"),
        };

        var loaded = GameNetworkJson.Deserialize(GameNetworkJson.Serialize(original));

        Assert.NotNull(loaded);
        var consumers = loaded.Consumers.Value!;
        Assert.Equal(2, consumers.Count);
        Assert.Equal(original.Consumers.Value![0].Name, consumers[0].Name);
        Assert.Equal(original.Consumers.Value[0].Services, consumers[0].Services);
        Assert.Equal(NetworkConsumerKind.Downloader, consumers[0].Kind);
        Assert.Equal(812.5, consumers[0].KBytesPerSecond);
        Assert.Null(consumers[1].Kind);
        Assert.Empty(consumers[1].Services);
    }

    [Fact]
    public void Newer_schema_is_not_loaded()
    {
        var json = GameNetworkJson.Serialize(Sample())
            .Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\": 2", json, StringComparison.Ordinal);

        Assert.Null(GameNetworkJson.Deserialize(json));
    }

    [Fact]
    public void Broken_json_is_not_loaded()
    {
        Assert.Null(GameNetworkJson.Deserialize("{"));
        Assert.Null(GameNetworkJson.Deserialize("[]"));
        Assert.Null(GameNetworkJson.Deserialize("{\"schemaVersion\": 1}"));
    }

    [Theory]
    [InlineData("server")]
    [InlineData("series")]
    [InlineData("wifi")]
    [InlineData("consumers")]
    [InlineData("activeActions")]
    [InlineData("boosterCpuPercent")]
    [InlineData("id")]
    public void Explicit_null_for_non_nullable_property_is_not_loaded(string property)
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(GameNetworkJson.Serialize(Sample()))!.AsObject();
        document[property] = null;

        Assert.Null(GameNetworkJson.Deserialize(document.ToJsonString()));
    }

    [Theory]
    [InlineData("server")]
    [InlineData("series")]
    [InlineData("wifi")]
    [InlineData("end")]
    [InlineData("interval")]
    public void Missing_required_property_is_not_loaded(string property)
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(GameNetworkJson.Serialize(Sample()))!.AsObject();
        document.Remove(property);

        Assert.Null(GameNetworkJson.Deserialize(document.ToJsonString()));
    }

    [Fact]
    public void Null_list_element_is_not_loaded()
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(GameNetworkJson.Serialize(Sample()))!.AsObject();
        document["series"]!.AsArray()[0] = null;

        Assert.Null(GameNetworkJson.Deserialize(document.ToJsonString()));
    }

    [Fact]
    public void Null_adapter_gateway_is_not_loaded()
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(GameNetworkJson.Serialize(Sample()))!.AsObject();
        document["adapter"]!["gateways"]!.AsArray()[0] = null;

        Assert.Null(GameNetworkJson.Deserialize(document.ToJsonString()));
    }

    [Fact]
    public void Measurement_without_end_series_is_rejected()
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(GameNetworkJson.Serialize(Sample()))!.AsObject();
        var series = document["series"]!.AsArray();
        for (var i = series.Count - 1; i >= 0; i--)
        {
            if (series[i]!["kind"]!.GetValue<string>() is "Host" or "LastHop")
                series.RemoveAt(i);
        }
        Assert.Equal(2, series.Count);

        Assert.Null(GameNetworkJson.Deserialize(document.ToJsonString()));
    }

    [Fact]
    public void Wrongly_typed_reading_is_not_loaded()
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(GameNetworkJson.Serialize(Sample()))!.AsObject();
        document["boosterCpuPercent"]!["source"] = 5;

        Assert.Null(GameNetworkJson.Deserialize(document.ToJsonString()));
    }

    private sealed class ReadingStatusComparer<T> : IEqualityComparer<Reading<T>>
    {
        public bool Equals(Reading<T> x, Reading<T> y) =>
            x.Status == y.Status && x.Source == y.Source && x.Note == y.Note;

        public int GetHashCode(Reading<T> obj) => (obj.Status, obj.Source, obj.Note).GetHashCode();
    }
}
