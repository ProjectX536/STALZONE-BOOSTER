using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Profiles;

public sealed record ProfileSet(IReadOnlyList<GamingProfile> Profiles, IReadOnlyList<ConfigWarning> Warnings)
{
    public GamingProfile? Find(string profileId) =>
        Profiles.FirstOrDefault(p => string.Equals(p.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
}

public interface IProfileRepository
{
    ProfileSet Current { get; }

    event EventHandler? Changed;

    Task SaveUserChoicesAsync(string profileId, ProfileUserChoices choices, CancellationToken ct);
}

public sealed partial class ProfileRepository : IProfileRepository
{
    private const string SchemaVersionKey = "schemaVersion";

    private static readonly JsonSerializerOptions UserFileWriteOptions = new(ConfigJson.SerializerOptions)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IProfileFileSource _files;
    private readonly ILogger<ProfileRepository> _logger;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private volatile Loaded _loaded;

    public ProfileRepository(IProfileFileSource files, ILogger<ProfileRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(logger);
        _files = files;
        _logger = logger;
        _loaded = Load();
    }

    public event EventHandler? Changed;

    public ProfileSet Current => _loaded.Set;

    public async Task SaveUserChoicesAsync(string profileId, ProfileUserChoices choices, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(choices);

        await _saveLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var loaded = _loaded;
            if (loaded.Set.Find(profileId) is not { } current || !loaded.FileByProfileId.TryGetValue(current.ProfileId, out var fileName))
                throw new ArgumentException($"Unknown profile '{profileId}'", nameof(profileId));
            if (loaded.RejectedUserFiles.TryGetValue(fileName, out var problem))
                throw new ProfileFileRejectedException(fileName, $"{problem.Kind}: {problem.Detail}");

            var target = current with
            {
                Games = choices.Games,
                AutoStart = choices.AutoStart,
                EnabledOptionalActions = choices.EnabledOptionalActions,
                BackgroundApps = current.BackgroundApps with { UserSelected = choices.BackgroundApps },
            };
            var builtIn = JsonNode.Parse(_files.ReadBuiltIn(fileName), documentOptions: ConfigJson.DocumentOptions) as JsonObject
                ?? throw new JsonException($"{fileName}: built-in profile is not a JSON object");

            var overlay = new JsonObject { [SchemaVersionKey] = GamingProfile.CurrentSchemaVersion };
            foreach (var (name, value) in JsonMerge.Diff(builtIn, JsonSerializer.SerializeToNode(target, UserFileWriteOptions)!.AsObject()))
            {
                if (name != SchemaVersionKey)
                    overlay[name] = value?.DeepClone();
            }

            await _files.WriteUserAsync(fileName, overlay.ToJsonString(UserFileWriteOptions), ct).ConfigureAwait(false);
            _loaded = Load();
        }
        finally
        {
            _saveLock.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Loaded Load()
    {
        var profiles = new List<GamingProfile>();
        var warnings = new List<ConfigWarning>();
        var fileByProfileId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rejected = new Dictionary<string, ConfigWarning>(StringComparer.OrdinalIgnoreCase);
        foreach (var fileName in _files.ListBuiltIn())
        {
            var loaded = LoadFile(fileName);

            if (loaded.Warnings.Count > 0)
                rejected[fileName] = loaded.Warnings[0];
            var normalized = ProfileNormalizer.Normalize(loaded.Value, fileName);
            var profile = normalized.Value;
            if (fileByProfileId.TryGetValue(profile.ProfileId, out var first))
            {
                warnings.Add(new(ConfigWarningKind.InvalidValue, fileName,
                    $"profileId '{profile.ProfileId}' is already used by {first}; profile skipped"));
                continue;
            }

            fileByProfileId[profile.ProfileId] = fileName;
            profiles.Add(profile);
            warnings.AddRange(loaded.Warnings);
            warnings.AddRange(normalized.Warnings);
        }

        foreach (var warning in warnings)
            LogWarning(warning.File, warning.Kind, warning.Detail);
        return new(new ProfileSet(profiles, warnings), fileByProfileId, rejected);
    }

    private ConfigLoadResult<GamingProfile> LoadFile(string fileName)
    {
        var builtIn = _files.ReadBuiltIn(fileName);
        string? user;
        try
        {
            user = _files.ReadUser(fileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var fallback = ConfigLoader.Load<GamingProfile>(fileName, builtIn, null, GamingProfile.CurrentSchemaVersion, []);
            return fallback with { Warnings = [new(ConfigWarningKind.Unreadable, fileName, ex.Message)] };
        }

        return ConfigLoader.Load<GamingProfile>(fileName, builtIn, user, GamingProfile.CurrentSchemaVersion, []);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Profile {File}: {Kind}: {Detail}")]
    private partial void LogWarning(string file, ConfigWarningKind kind, string detail);

    private sealed record Loaded(ProfileSet Set, IReadOnlyDictionary<string, string> FileByProfileId,
        IReadOnlyDictionary<string, ConfigWarning> RejectedUserFiles);
}
