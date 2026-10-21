using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.Inventory;
using Booster.Core.Profiles;
using Booster.Core.Reports;

namespace Booster.Core.SystemHealth;

public enum TweakState
{
    Proposed,

    ManualOnly,

    NotNeeded,
}

public sealed record TweakProposal(RegistryTweakDefinition Definition, TweakState State, string? ValueName, string? Game,
    IReadOnlyList<string> Evidence);

public static class TweakAdvisor
{
    // Так PresentMon называет показ копированием через композитор (blt в окне).
    private const string LegacyCopyModePrefix = "Composed: Copy";

    public static IReadOnlyList<TweakProposal> Propose(SystemSnapshot snapshot, BenchmarkSeries? series, IReadOnlyList<string> gameNames,
        IReadOnlyList<RegistryTweakDefinition> tweaks)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(gameNames);
        ArgumentNullException.ThrowIfNull(tweaks);

        var build = snapshot.Hardware.Os is { IsAvailable: true, Value.BuildNumber: { IsAvailable: true, Value: var number } }
            ? number
            : (int?)null;
        var gameSeries = ProfileGameSeries(series, gameNames);
        var proposals = new List<TweakProposal>();
        foreach (var tweak in tweaks)
        {
            Found? found;
            switch (tweak.Id)
            {
                case RegistryTweakIds.GameMode:
                    found = GameModeOff(snapshot, tweak);
                    break;
                case RegistryTweakIds.GpuHighPerformance:
                    found = gameSeries is null ? null : IntegratedRender(snapshot, gameSeries);
                    break;
                case RegistryTweakIds.WindowedFlipModel:
                    found = gameSeries is null ? null : LegacyCopyPresent(snapshot, gameSeries);
                    break;
                default:
                    continue;
            }

            proposals.Add(found is null
                ? new TweakProposal(tweak, TweakState.NotNeeded, tweak.ValueName, null, [])
                : new TweakProposal(tweak, build >= tweak.MinBuild ? TweakState.Proposed : TweakState.ManualOnly, found.ValueName,
                    found.Game, found.Evidence));
        }

        return proposals;
    }

    public static IReadOnlyList<SystemAdvice> Advise(IReadOnlyList<TweakProposal> proposals)
    {
        ArgumentNullException.ThrowIfNull(proposals);

        var advice = new List<SystemAdvice>();
        foreach (var proposal in proposals.Where(p => p.State != TweakState.NotNeeded))
        {
            (AdviceId Id, FindingSeverity Severity)? kind = proposal.Definition.Id switch
            {
                RegistryTweakIds.GameMode => (AdviceId.GameModeOff, FindingSeverity.Info),
                RegistryTweakIds.GpuHighPerformance => (AdviceId.GpuOnIntegrated, FindingSeverity.Warning),
                RegistryTweakIds.WindowedFlipModel => (AdviceId.LegacyWindowedPresent, FindingSeverity.Info),
                _ => null,
            };
            if (kind is not { } value)
                continue;

            var level = proposal.State == TweakState.Proposed ? SafetyLevel.L2Confirmed : SafetyLevel.L3ManualOnly;
            advice.Add(new SystemAdvice(value.Id, AdviceModule.Tweaks, value.Severity, level, proposal.Evidence));
        }

        return advice;
    }

    private sealed record Found(string? ValueName, string? Game, IReadOnlyList<string> Evidence);

    private static Found? GameModeOff(SystemSnapshot snapshot, RegistryTweakDefinition tweak) =>
        snapshot.System.Gaming is { IsAvailable: true, Value.GameMode: { IsAvailable: true, Value: false } }
            ? new Found(tweak.ValueName, null, [])
            : null;

    private static BenchmarkSeries? ProfileGameSeries(BenchmarkSeries? series, IReadOnlyList<string> gameNames)
    {
        if (series is not { HasValidRuns: true } || ProcessNames.Normalize(series.Conditions.Target) is not { } target)
            return null;
        return gameNames.Any(name => string.Equals(ProcessNames.Normalize(name), target, StringComparison.OrdinalIgnoreCase))
            ? series
            : null;
    }

    private static Found? IntegratedRender(SystemSnapshot snapshot, BenchmarkSeries series)
    {
        if (snapshot.Hardware.Gpu is not { IsAvailable: true, Value: { IsHybrid: { IsAvailable: true, Value: true } } gpu }
            || !gpu.Adapters.Any(a => IsKind(a, GpuAdapterKind.Discrete))
            || series.Conditions.RenderAdapter is not { IsAvailable: true, Value: { } adapter }
            || !gpu.Adapters.Any(a => IsKind(a, GpuAdapterKind.Integrated)
                && string.Equals(a.Name.Trim(), adapter.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var target = series.Conditions.Target;
        var preferences = GamePreferences(gpu, target);
        if (preferences.Any(p => p.Preference == AppGpuPreferenceKind.PowerSaving))
            return null;
        return new Found(preferences.FirstOrDefault()?.ExecutablePath, target, [target, adapter]);
    }

    private static Found? LegacyCopyPresent(SystemSnapshot snapshot, BenchmarkSeries series)
    {
        var mode = series.Conditions.PresentModes.FirstOrDefault(m => m.StartsWith(LegacyCopyModePrefix, StringComparison.OrdinalIgnoreCase));
        if (mode is null)
            return null;

        var target = series.Conditions.Target;
        var path = snapshot.Hardware.Gpu is { IsAvailable: true, Value: { } gpu } ? GamePreferences(gpu, target).FirstOrDefault()?.ExecutablePath : null;
        return new Found(path, target, [target, mode]);
    }

    private static bool IsKind(GpuAdapterInfo adapter, GpuAdapterKind kind) => adapter.Kind is { IsAvailable: true, Value: var value } && value == kind;

    private static List<AppGpuPreference> GamePreferences(GpuInventory gpu, string target)
    {
        var game = ProcessNames.Normalize(target);
        var preferences = gpu.AppPreferences is { IsAvailable: true, Value: { } list } ? list : [];
        return game is null
            ? []
            : [.. preferences.Where(p => string.Equals(ProcessNames.Normalize(FileName(p.ExecutablePath)), game, StringComparison.OrdinalIgnoreCase))];
    }

    // Вне Windows Path.GetFileName не считает «\» разделителем.
    private static string FileName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
}
