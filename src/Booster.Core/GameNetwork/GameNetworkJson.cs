using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Booster.Core.Serialization;
using Booster.Core.SystemHealth;

namespace Booster.Core.GameNetwork;

public static class GameNetworkJson
{
    private const string SchemaVersionProperty = "schemaVersion";

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

    public static string Serialize(GameNetworkMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        return JsonSerializer.Serialize(measurement, Options);
    }

    /// <summary>Null: файл повреждён или записан более новой схемой.</summary>
    public static GameNetworkMeasurement? Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            if (JsonNode.Parse(json) is not JsonObject document)
                return null;
            if (document[SchemaVersionProperty] is not JsonValue version || !version.TryGetValue<int>(out var schemaVersion))
                return null;
            if (schemaVersion > GameNetworkMeasurement.CurrentSchemaVersion)
                return null;
            var measurement = document.Deserialize<GameNetworkMeasurement>(Options);
            return measurement is not null && IsComplete(measurement) ? measurement : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Аннотации nullable System.Text.Json не проверяет для коллекций и параметров конструктора записей.
    private static bool IsComplete(GameNetworkMeasurement m) =>
        m.Id is not null
        && m.Server is { Address: not null, Candidates: not null } server && server.Candidates.All(c => c?.Address is not null)
        && m.Series is not null && m.Series.All(s => s is { Address: not null, SamplesMs: not null })
        && m.Series.Any(s => s.Kind is NetworkTargetKind.Host or NetworkTargetKind.LastHop)
        && (m.Route is null || m.Route is { Target: not null, Hops: not null } route
            && route.Hops.All(h => h is { ProbesMs: not null }))
        && (m.Adapter is null || m.Adapter is { Id: not null, Name: not null, Description: not null, Gateways: not null } adapter
            && adapter.Gateways.All(g => g is not null))
        && m.ActiveActions is not null && m.ActiveActions.All(a => a is not null)
        && (m.Consumers.Value is null || m.Consumers.Value.All(c => c is { Name: not null, Services: not null }
                                                                    && c.Services.All(s => s is not null)));
}
