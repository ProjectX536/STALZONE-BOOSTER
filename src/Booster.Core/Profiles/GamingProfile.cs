using System.Text.Json.Serialization;
using Booster.Core.Abstractions;

namespace Booster.Core.Profiles;

/// <summary>Игровой профиль: <c>config/Profiles/*.json</c>. Значения и их обоснования — в самом файле.</summary>
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

/// <param name="Reversible">Справочное поле: обратимость определяет <see cref="ActionDescriptor"/> в коде.</param>
/// <param name="Value">Параметр действия (для <c>game-priority</c> — класс приоритета).</param>
/// <param name="TargetSchemes">GUID схем питания для <c>power-plan</c>, по убыванию предпочтения.</param>
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

/// <summary>Выбор пользователя на экране профилей; остальное в профиле пользователь меняет только руками в файле.</summary>
public sealed record ProfileUserChoices(
    IReadOnlyList<string> Games,
    bool AutoStart,
    IReadOnlyList<string> EnabledOptionalActions,
    IReadOnlyList<string> BackgroundApps);
