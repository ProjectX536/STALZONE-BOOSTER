using System.Text.Json.Nodes;

namespace Booster.Core.Configuration;

public static class ConfigFiles
{
    public const string Settings = "settings.json";
    public const string Thresholds = "thresholds.json";
    public const string OfficialSources = "official-sources.json";
    public const string SystemCatalog = "system-catalog.json";
}

public enum ConfigWarningKind
{
    InvalidJson,
    MissingSchemaVersion,
    NewerSchemaVersion,
    MigrationFailed,
    ValueClamped,

    Unreadable,

    InvalidValue,

    UserFileIgnored,
}

public sealed record ConfigWarning(ConfigWarningKind Kind, string File, string Detail);

public sealed record ConfigLoadResult<T>(T Value, IReadOnlyList<ConfigWarning> Warnings);

public interface IConfigMigration
{
    int FromVersion { get; }

    JsonObject Migrate(JsonObject document);
}

public interface IConfigFileSource
{
    string ReadBuiltIn(string fileName);

    string? ReadUser(string fileName);

    Task WriteUserAsync(string fileName, string json, CancellationToken ct);
}

public sealed record BoosterConfigurationSnapshot(
    BoosterSettings Settings,
    Thresholds Thresholds,
    OfficialSources Sources,
    SystemCatalog Catalog,
    IReadOnlyList<ConfigWarning> Warnings);
