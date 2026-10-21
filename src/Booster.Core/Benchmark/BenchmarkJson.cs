using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Booster.Core.Abstractions;

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

    private sealed class ReadingJsonConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Reading<>);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(ReadingJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()))!;
    }

    private sealed class ReadingJsonConverter<T> : JsonConverter<Reading<T>>
    {
        public override Reading<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("A reading must be an object.");

            T? value = default;
            var hasValue = false;
            DataStatus? status = null;
            string? source = null;
            string? note = null;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString();
                reader.Read();
                switch (name)
                {
                    case "value":
                        value = reader.TokenType == JsonTokenType.Null ? default : JsonSerializer.Deserialize<T>(ref reader, options);
                        hasValue = value is not null;
                        break;
                    case "status":
                        status = JsonSerializer.Deserialize<DataStatus>(ref reader, options);
                        break;
                    case "source":
                        source = reader.GetString();
                        break;
                    case "note":
                        note = reader.GetString();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (status is not { } known)
                throw new JsonException("A reading has no status.");
            if (known == DataStatus.Available && !hasValue)
                throw new JsonException("An available reading has no value.");
            return new Reading<T>(known == DataStatus.Available ? value : default, known, source, note);
        }

        public override void Write(Utf8JsonWriter writer, Reading<T> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("value");
            if (value is { IsAvailable: true, Value: { } known })
                JsonSerializer.Serialize(writer, known, options);
            else
                writer.WriteNullValue();
            writer.WriteString("status", value.Status.ToString());
            writer.WriteString("source", value.Source);
            writer.WriteString("note", value.Note);
            writer.WriteEndObject();
        }
    }
}
