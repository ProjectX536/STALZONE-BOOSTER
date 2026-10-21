using System.Text.Json;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Sensors;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.Benchmark;

public class BenchmarkJsonTests
{
    private const string Source = SampleSeries.Source;

    private static BenchmarkSeries FullSeries() => SampleSeries.Full();

    [Fact]
    public void Series_round_trips_with_readings_and_enums()
    {
        var series = FullSeries();

        var json = BenchmarkJson.Serialize(series);
        var restored = BenchmarkJson.Deserialize(json);

        Assert.Equal(json, BenchmarkJson.Serialize(restored));
        Assert.Equal(series.StartedAt, restored.StartedAt);
        Assert.Equal(series.Schedule, restored.Schedule);
        Assert.Equal(PowerSource.Battery, restored.Conditions.PowerSource);
        Assert.Equal(new DisplayMode("Monitor", 2560, 1440, 143.856, false, null), restored.Conditions.Display.Value);
        Assert.Equal(Reading<string>.Failure("PowerGetActiveScheme", "read failed"), restored.Conditions.PowerPlan);
        Assert.Equal(Reading<double>.Unsupported(Source, "no counter"), restored.Telemetry.VramUsedPeakPercent);
        Assert.Equal(Reading<double>.Known(0, Source), restored.Telemetry.PageReadsPerSecond);
        Assert.Equal(ThrottleSignal.GpuDriverPowerLimit, Assert.Single(restored.Telemetry.Throttle!.Findings).Signal);
        Assert.Null(restored.Runs[1].Metrics.Metrics);
        Assert.Equal(80.5, restored.Runs[0].Metrics.Metrics!.OnePercentLowFps);
        Assert.Equal([null, BottleneckClass.GpuLimited], restored.Recommendations.Select(r => r.Because));
        Assert.Equal("Новый драйвер", restored.Conditions.Notes.Change);
        Assert.Contains("\"status\": \"NotSupported\"", json, StringComparison.Ordinal);
        Assert.Contains("\"powerSource\": \"Battery\"", json, StringComparison.Ordinal);
        Assert.Contains("Город", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Value_of_unavailable_reading_is_not_kept()
    {
        var series = FullSeries();
        var stale = series with { Conditions = series.Conditions with { DriverVersion = new Reading<string>("old", DataStatus.Unknown, Source) } };

        var restored = BenchmarkJson.Deserialize(BenchmarkJson.Serialize(stale));

        Assert.Equal(Reading<string>.Unknown(Source), restored.Conditions.DriverVersion);
    }

    [Fact]
    public void Series_without_latency_fields_loads_as_unknown()
    {
        string[] latencyFields = ["dpcTimeMedianPercent", "dpcTimePeakPercent", "interruptTimeMedianPercent", "interruptTimePeakPercent"];
        var document = System.Text.Json.Nodes.JsonNode.Parse(BenchmarkJson.Serialize(FullSeries()))!;
        var telemetry = document["series"]!["telemetry"]!.AsObject();
        foreach (var field in latencyFields)
            Assert.True(telemetry.Remove(field), field);

        var restored = BenchmarkJson.Deserialize(document.ToJsonString()).Telemetry;

        Assert.False(restored.DpcTimeMedianPercent.IsAvailable);
        Assert.False(restored.DpcTimePeakPercent.IsAvailable);
        Assert.False(restored.InterruptTimeMedianPercent.IsAvailable);
        Assert.False(restored.InterruptTimePeakPercent.IsAvailable);
        Assert.Equal(DataStatus.Unknown, restored.DpcTimePeakPercent.Status);
    }

    [Fact]
    public void Document_carries_schema_version()
    {
        using var document = JsonDocument.Parse(BenchmarkJson.Serialize(FullSeries()));

        Assert.Equal(BenchmarkJson.CurrentSchemaVersion, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("20261020-120000-game", document.RootElement.GetProperty("series").GetProperty("id").GetString());
    }

    [Fact]
    public void Future_schema_version_is_rejected()
    {
        var json = BenchmarkJson.Serialize(FullSeries()).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);

        Assert.Throws<NotSupportedException>(() => BenchmarkJson.Deserialize(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"series\":{}}")]
    [InlineData("{\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"series\":null}")]
    [InlineData("{\"schemaVersion\":\"1\",\"series\":{}}")]
    [InlineData("{\"schemaVersion\":1,\"series\":{\"id\":\"x\"}}")]
    public void Malformed_document_is_a_json_exception(string json)
    {
        Assert.ThrowsAny<JsonException>(() => BenchmarkJson.Deserialize(json));
    }

    [Fact]
    public void Reading_without_status_is_a_json_exception()
    {
        var json = BenchmarkJson.Serialize(FullSeries()).Replace("\"status\": \"NotSupported\",", "", StringComparison.Ordinal);

        Assert.ThrowsAny<JsonException>(() => BenchmarkJson.Deserialize(json));
    }
}
