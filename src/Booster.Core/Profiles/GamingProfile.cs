using System.Text.Json.Serialization;
using Booster.Core.Abstractions;

namespace Booster.Core.Profiles;

public sealed record GamingProfile(
    int SchemaVersion,
    string ProfileId,
    string Name,
    IReadOnlyList<string> Games,
    bool AutoStart,
    IReadOnlyList<string> EnabledOptionalActions,
    ProfileMonitoring Monitoring,
    IReadOnlyList<ProfileActionEntry> Actions,
    BackgroundAppsSettings BackgroundApps,
    ProfileRecovery Recovery)
{
    public const int CurrentSchemaVersion = 1;

    public ProfileActionEntry? FindAction(string actionId) =>
        Actions.FirstOrDefault(a => string.Equals(a.Id, actionId, StringComparison.Ordinal));
}

public sealed record ProfileMonitoring(
    bool Enabled,
    int SampleIntervalMs,
    bool CollectFrameMetrics,
    bool CollectCpuMetrics,
    bool CollectMemoryMetrics,
    bool CollectGpuTelemetry);

public sealed record ProfileActionEntry(
    string Id,
    [property: JsonConverter(typeof(ActionModeJsonConverter))] ActionMode Mode,
    bool? Reversible = null,
    string? Value = null,
    IReadOnlyList<string>? TargetSchemes = null);

public sealed record BackgroundAppsSettings(int CloseTimeoutMs, IReadOnlyList<string> UserSelected);

public sealed record ProfileRecovery(
    bool SnapshotBeforeChanges,
    bool RestoreOnGameExit,
    bool RecoverInterruptedSession,
    bool VerifyAfterRestore);

public sealed record ProfileUserChoices(
    IReadOnlyList<string> Games,
    bool AutoStart,
    IReadOnlyList<string> EnabledOptionalActions,
    IReadOnlyList<string> BackgroundApps);
