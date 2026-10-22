using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Booster.Core.Abstractions;
using Booster.Core.Serialization;

namespace Booster.Core.Benchmark;

public static class BenchmarkJson
{
    public const int CurrentSchemaVersion = 1;

    private const string SchemaVersionProperty = "schemaVersion";
    private const string SeriesProperty = "series";

    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            RespectRequiredConstructorParameters = true,
            Converters = { new JsonStringEnumConverter(), new ReadingJsonConverterFactory() },
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    public static string Serialize(BenchmarkSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);
        var document = new JsonObject
        {
            [SchemaVersionProperty] = CurrentSchemaVersion,
            [SeriesProperty] = JsonSerializer.SerializeToNode(series, Options),
        };
        return document.ToJsonString(Options);
    }

    public static BenchmarkSeries Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (JsonNode.Parse(json) is not JsonObject document)
            throw new JsonException("The benchmark file is not a JSON object.");
        if (document[SchemaVersionProperty] is not JsonValue version || !version.TryGetValue<int>(out var schemaVersion))
            throw new JsonException("The benchmark file has no numeric schemaVersion.");
        if (schemaVersion > CurrentSchemaVersion)
            throw new NotSupportedException(
                $"The benchmark file has schema version {schemaVersion}; this Booster reads up to {CurrentSchemaVersion}.");
        return document[SeriesProperty]?.Deserialize<BenchmarkSeries>(Options)
               ?? throw new JsonException("The benchmark file has no series.");
    }
}
