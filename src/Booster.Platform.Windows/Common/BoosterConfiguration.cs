using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Booster.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Booster.Platform.Windows.Common;

/// <summary>
/// Три файла конфигурации: встроенные значения, поверх них — пользовательские. Любая проблема
/// пользовательского файла даёт предупреждение и встроенные значения этого файла, но не мешает старту.
/// </summary>
public sealed partial class BoosterConfiguration : IBoosterConfiguration
{
    private const string SchemaVersionKey = "schemaVersion";

    // Кириллица в путях остаётся читаемой в файле, который пользователь может открыть руками.
    private static readonly JsonSerializerOptions UserFileWriteOptions = new(ConfigJson.SerializerOptions)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IConfigFileSource _files;
    private readonly ILogger<BoosterConfiguration> _logger;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private volatile BoosterConfigurationSnapshot _current;

    public BoosterConfiguration(IConfigFileSource files, ILogger<BoosterConfiguration> logger)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(logger);
        _files = files;
        _logger = logger;
        _current = Load();
    }

    public BoosterConfigurationSnapshot Current => _current;

    /// <summary>
    /// Пишет в пользовательский файл только отличия от встроенного, чтобы новые встроенные значения
    /// следующих версий доходили до пользователя. Затем перечитывает конфигурацию.
    /// </summary>
    public async Task SaveUserSettingsAsync(BoosterSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _saveLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var builtIn = JsonNode.Parse(_files.ReadBuiltIn(ConfigFiles.Settings), documentOptions: ConfigJson.DocumentOptions)
                    as JsonObject
                ?? throw new JsonException($"{ConfigFiles.Settings}: built-in configuration is not a JSON object");
            var target = JsonSerializer.SerializeToNode(settings, ConfigJson.SerializerOptions)!.AsObject();

            var overlay = new JsonObject { [SchemaVersionKey] = BoosterSettings.CurrentSchemaVersion };
            foreach (var (name, value) in JsonMerge.Diff(builtIn, target))
            {
                if (name != SchemaVersionKey)
                    overlay[name] = value?.DeepClone();
            }

            await _files.WriteUserAsync(ConfigFiles.Settings, overlay.ToJsonString(UserFileWriteOptions), ct).ConfigureAwait(false);
            _current = Load();
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private BoosterConfigurationSnapshot Load()
    {
        var settings = LoadFile<BoosterSettings>(ConfigFiles.Settings, BoosterSettings.CurrentSchemaVersion);
        var normalized = SettingsNormalizer.Normalize(settings.Value);
        var thresholds = LoadFile<Thresholds>(ConfigFiles.Thresholds, Thresholds.CurrentSchemaVersion);
        var normalizedThresholds = ThresholdsNormalizer.Normalize(thresholds.Value);
        var sources = LoadFile<OfficialSources>(ConfigFiles.OfficialSources, OfficialSources.CurrentSchemaVersion);
        var catalog = LoadBuiltInOnly<SystemCatalog>(ConfigFiles.SystemCatalog, SystemCatalog.CurrentSchemaVersion);

        ConfigWarning[] warnings =
        [
            .. settings.Warnings, .. normalized.Warnings, .. thresholds.Warnings, .. normalizedThresholds.Warnings, .. sources.Warnings,
            .. catalog.Warnings,
        ];
        foreach (var warning in warnings)
            LogWarning(warning.File, warning.Kind, warning.Detail);

        return new(normalized.Value, normalizedThresholds.Value, sources.Value, catalog.Value, warnings);
    }

    /// <summary>
    /// Справочник политики безопасности: пользовательский файл не применяется, чтобы правка конфигурации не снимала защиту.
    /// Если он есть — только предупреждение.
    /// </summary>
    private ConfigLoadResult<T> LoadBuiltInOnly<T>(string fileName, int supportedVersion)
    {
        var loaded = ConfigLoader.Load<T>(fileName, _files.ReadBuiltIn(fileName), null, supportedVersion, []);
        string? user;
        try
        {
            user = _files.ReadUser(fileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Файл всё равно не применяется: его недоступность ничего не меняет.
            return loaded;
        }
        return user is null
            ? loaded
            : loaded with { Warnings = [.. loaded.Warnings, new(ConfigWarningKind.UserFileIgnored, fileName, "safety policy: only the built-in catalog applies")] };
    }

    private ConfigLoadResult<T> LoadFile<T>(string fileName, int supportedVersion)
    {
        var builtIn = _files.ReadBuiltIn(fileName);
        string? user;
        try
        {
            user = _files.ReadUser(fileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var fallback = ConfigLoader.Load<T>(fileName, builtIn, null, supportedVersion, []);
            return fallback with { Warnings = [new(ConfigWarningKind.Unreadable, fileName, ex.Message)] };
        }

        // Миграций пока нет: все схемы — версии 1.
        return ConfigLoader.Load<T>(fileName, builtIn, user, supportedVersion, []);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Configuration {File}: {Kind}: {Detail}")]
    private partial void LogWarning(string file, ConfigWarningKind kind, string detail);
}
