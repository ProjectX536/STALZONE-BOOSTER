using Booster.Core.Abstractions;
using Booster.Core.Profiles;

namespace Booster.Core.Tests.TestSupport;

public static class ProfileFactory
{
    public const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    public static IReadOnlyList<ProfileActionEntry> DefaultActions { get; } =
    [
        new(ActionIds.PowerPlan, ActionMode.AskFirst, Reversible: true, TargetSchemes: [HighPerformance]),
        new(ActionIds.GamePriority, ActionMode.Optional, Reversible: true, Value: "AboveNormal"),
        new(ActionIds.BackgroundApps, ActionMode.AskFirst, Reversible: false),
        new("registry-tweaks", ActionMode.RecommendOnly),
        new("driver-updates", ActionMode.RecommendOnly),
        new("pagefile", ActionMode.RecommendOnly),
        new("windows-services", ActionMode.RecommendOnly),
    ];

    public static string ShippedCompetitiveJson() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config", "Profiles", "Competitive.json"));

    public static GamingProfile Profile(
        IReadOnlyList<string>? games = null,
        IReadOnlyList<ProfileActionEntry>? actions = null,
        IReadOnlyList<string>? userSelected = null,
        IReadOnlyList<string>? enabledOptional = null,
        bool autoStart = false,
        int sampleIntervalMs = 2000,
        int closeTimeoutMs = 10000,
        ProfileRecovery? recovery = null,
        bool monitoringEnabled = true,
        string profileId = "competitive") =>
        new(GamingProfile.CurrentSchemaVersion, profileId, "Competitive Gaming", games ?? [], autoStart, enabledOptional ?? [],
            new ProfileMonitoring(monitoringEnabled, sampleIntervalMs, true, true, true, true),
            actions ?? DefaultActions,
            new BackgroundAppsSettings(closeTimeoutMs, userSelected ?? []),
            recovery ?? new ProfileRecovery(true, true, true, true));

    public static IReadOnlyList<ProfileActionEntry> ActionsWith(params ProfileActionEntry[] replacements) =>
        DefaultActions.Select(a => replacements.FirstOrDefault(r => r.Id == a.Id) ?? a).ToArray();
}
