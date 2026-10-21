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

    /// <summary>Пользовательский файл есть, но прочитать его не удалось (занят, нет доступа).</summary>
    Unreadable,

    /// <summary>Значение недопустимо и удалено (или запись пропущена); остальной файл применён.</summary>
    InvalidValue,

    /// <summary>Пользовательский файл не применяется по политике безопасности; действуют встроенные значения.</summary>
    UserFileIgnored,
}

/// <summary>Замечание к пользовательской конфигурации. <see cref="Detail"/> — техническая подробность для лога и отчёта.</summary>
public sealed record ConfigWarning(ConfigWarningKind Kind, string File, string Detail);

public sealed record ConfigLoadResult<T>(T Value, IReadOnlyList<ConfigWarning> Warnings);

/// <summary>Переводит документ из версии <see cref="FromVersion"/> в <c>FromVersion + 1</c>. Документ можно менять на месте.</summary>
public interface IConfigMigration
{
    int FromVersion { get; }

    JsonObject Migrate(JsonObject document);
}

/// <summary>Доступ к файлам конфигурации: встроенные лежат рядом с exe, пользовательские — в профиле.</summary>
public interface IConfigFileSource
{
    string ReadBuiltIn(string fileName);

    /// <summary>null — пользовательского файла нет.</summary>
    string? ReadUser(string fileName);

    Task WriteUserAsync(string fileName, string json, CancellationToken ct);
}

public sealed record BoosterConfigurationSnapshot(
    BoosterSettings Settings,
    Thresholds Thresholds,
    OfficialSources Sources,
    SystemCatalog Catalog,
    IReadOnlyList<ConfigWarning> Warnings);
