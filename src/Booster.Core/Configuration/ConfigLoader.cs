using System.Text.Json;
using System.Text.Json.Nodes;

namespace Booster.Core.Configuration;

/// <summary>
/// Загружает файл конфигурации: встроенный документ, поверх него — пользовательский.
/// Пользовательский файл, который нельзя применить (не JSON, нет версии, версия новее,
/// миграция не удалась, значения не подходят по типу), пропускается целиком с предупреждением:
/// частично применённый файл хуже предсказуемых встроенных значений.
/// </summary>
public static class ConfigLoader
{
    private const string SchemaVersionKey = "schemaVersion";

    /// <exception cref="JsonException">Встроенный документ повреждён — это ошибка поставки, а не пользователя.</exception>
    public static ConfigLoadResult<T> Load<T>(string fileName, string builtInJson, string? userJson,
        int supportedVersion, IReadOnlyList<IConfigMigration> migrations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(builtInJson);
        ArgumentNullException.ThrowIfNull(migrations);

        var builtIn = ParseObject(builtInJson)
            ?? throw new JsonException($"{fileName}: built-in configuration is not a JSON object");
        if (userJson is null)
            return new(Deserialize<T>(builtIn), []);

        var (user, problem) = PrepareUser(fileName, userJson, supportedVersion, migrations);
        if (user is null)
            return new(Deserialize<T>(builtIn), [problem!]);

        try
        {
            return new(Deserialize<T>(JsonMerge.Merge(builtIn, user)), []);
        }
        catch (JsonException ex)
        {
            return new(Deserialize<T>(builtIn),
                [new ConfigWarning(ConfigWarningKind.InvalidJson, fileName, $"values do not match the schema: {ex.Message}")]);
        }
    }

    private static (JsonObject? Document, ConfigWarning? Problem) PrepareUser(string fileName, string userJson,
        int supportedVersion, IReadOnlyList<IConfigMigration> migrations)
    {
        JsonObject? user;
        try
        {
            user = ParseObject(userJson);
        }
        catch (JsonException ex)
        {
            return (null, new(ConfigWarningKind.InvalidJson, fileName, ex.Message));
        }

        if (user is null)
            return (null, new(ConfigWarningKind.InvalidJson, fileName, "root is not a JSON object"));

        if (user[SchemaVersionKey] is not JsonValue versionNode || !versionNode.TryGetValue<int>(out var version))
            return (null, new(ConfigWarningKind.MissingSchemaVersion, fileName, $"'{SchemaVersionKey}' is missing or not an integer"));

        if (version > supportedVersion)
            return (null, new(ConfigWarningKind.NewerSchemaVersion, fileName,
                $"schema version {version} is newer than supported {supportedVersion}"));

        for (var from = version; from < supportedVersion; from++)
        {
            var migration = migrations.FirstOrDefault(m => m.FromVersion == from);
            if (migration is null)
                return (null, new(ConfigWarningKind.MigrationFailed, fileName, $"no migration {from} -> {from + 1}"));

            try
            {
                user = migration.Migrate(user) ?? throw new InvalidOperationException("migration returned null");
            }
            catch (Exception ex)
            {
                return (null, new(ConfigWarningKind.MigrationFailed, fileName,
                    $"migration {from} -> {from + 1}: {ex.GetType().Name}: {ex.Message}"));
            }

            user[SchemaVersionKey] = from + 1;
        }

        return (user, null);
    }

    private static JsonObject? ParseObject(string json) =>
        JsonNode.Parse(json, documentOptions: ConfigJson.DocumentOptions) as JsonObject;

    private static T Deserialize<T>(JsonObject document) =>
        document.Deserialize<T>(ConfigJson.SerializerOptions)
            ?? throw new JsonException($"document deserialized to null as {typeof(T).Name}");
}
