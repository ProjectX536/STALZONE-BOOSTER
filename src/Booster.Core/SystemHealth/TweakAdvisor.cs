using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.Inventory;
using Booster.Core.Profiles;
using Booster.Core.Reports;

namespace Booster.Core.SystemHealth;

public enum TweakState
{
    /// <summary>Доказательство есть, сборка подходит — можно применить с подтверждением.</summary>
    Proposed,

    /// <summary>Доказательство есть, но сборка ниже MinBuild или неизвестна — только рекомендация.</summary>
    ManualOnly,

    /// <summary>Доказательства нет — ничего не предлагается.</summary>
    NotNeeded,
}

/// <summary>Настройка белого списка реестра и то, что Booster о ней знает по снимку и серии бенчмарка.</summary>
/// <param name="ValueName">Для пар — путь к exe, если он известен из UserGpuPreferences; null — путь выбирает пользователь.</param>
/// <param name="Game">Имя процесса игры из серии; null — у game-mode.</param>
/// <param name="Evidence">Аргументы текста: адаптер, режим показа и т.п.</param>
public sealed record TweakProposal(RegistryTweakDefinition Definition, TweakState State, string? ValueName, string? Game,
    IReadOnlyList<string> Evidence);

/// <summary>
/// Правила-доказательства белого списка реестра: настройка предлагается, только если снимок или серия бенчмарка
/// показывают то, что она исправляет. Выключенная настройка без такого доказательства — NotNeeded.
/// </summary>
public static class TweakAdvisor
{
    // Так PresentMon называет показ копированием через композитор — устаревшую модель blt в окне
    // («Composed: Copy with GPU GDI», «Composed: Copy with CPU GDI»). Это формат вывода PresentMon, а не порог.
    private const string LegacyCopyModePrefix = "Composed: Copy";

    /// <param name="series">Серия бенчмарка текущей сессии; null — правила по серии не срабатывают.</param>
    /// <param name="gameNames">Игры из профилей.</param>
    /// <returns>По записи на каждую настройку с известным Id, в порядке каталога.</returns>
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

    /// <summary>
    /// Модуль Tweaks. Proposed/ManualOnly → GameModeOff (Info), GpuOnIntegrated (Warning), LegacyWindowedPresent (Info).
    /// Level: Proposed → L2, ManualOnly → L3. Аргументы — <see cref="TweakProposal.Evidence"/>.
    /// </summary>
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

    // Unknown — не доказательство: значение вне 0/1 или сбой чтения не означают, что режим выключен.
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
        // Явный выбор «Энергосбережение» сделал пользователь: Booster его не перебивает.
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

    /// <summary>
    /// Записи «Параметры → Графика» для exe с тем же именем файла, что у игры. Список не прочитан — записей не видно:
    /// путь тогда выбирает пользователь и видит текущее значение в подтверждении.
    /// </summary>
    private static List<AppGpuPreference> GamePreferences(GpuInventory gpu, string target)
    {
        var game = ProcessNames.Normalize(target);
        var preferences = gpu.AppPreferences is { IsAvailable: true, Value: { } list } ? list : [];
        return game is null
            ? []
            : [.. preferences.Where(p => string.Equals(ProcessNames.Normalize(FileName(p.ExecutablePath)), game, StringComparison.OrdinalIgnoreCase))];
    }

    // Core собирается не только под Windows: Path.GetFileName вне Windows не делит путь по «\».
    private static string FileName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
}
