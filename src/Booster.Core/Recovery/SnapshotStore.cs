using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Booster.Core.Recovery;

public enum JournalProblemKind
{
    InvalidJson,
    NewerSchemaVersion,
    Unreadable,

    RecoveryFailed,
}

public sealed record JournalProblem(string FileName, JournalProblemKind Kind, string Detail);

public sealed record SessionReadResult(SessionRecord? Session, JournalProblem? Problem);

public static class SnapshotStore
{
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,

        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(SessionRecord session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return JsonSerializer.Serialize(session, SerializerOptions);
    }

    public static SessionReadResult Deserialize(string fileName, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object
                    || !doc.RootElement.TryGetProperty("schemaVersion", out var version)
                    || !version.TryGetInt32(out var schemaVersion) || schemaVersion < 1)
                    return Invalid(fileName, "no valid schemaVersion");
                if (schemaVersion > SessionRecord.CurrentSchemaVersion)
                    return new SessionReadResult(null, new JournalProblem(fileName, JournalProblemKind.NewerSchemaVersion,
                        $"schemaVersion {schemaVersion} > {SessionRecord.CurrentSchemaVersion}"));
            }

            var session = JsonSerializer.Deserialize<SessionRecord>(json, SerializerOptions);
            if (session is null)
                return Invalid(fileName, "null");

            if (!SessionIds.IsValid(session.SessionId) || !string.Equals(fileName, session.SessionId + ".json", StringComparison.OrdinalIgnoreCase))
                return Invalid(fileName, $"session id '{session.SessionId}' does not match the file name");
            return new SessionReadResult(session, null);
        }
        catch (JsonException ex)
        {
            return Invalid(fileName, ex.Message);
        }
    }

    private static SessionReadResult Invalid(string fileName, string detail) =>
        new(null, new JournalProblem(fileName, JournalProblemKind.InvalidJson, detail));
}
