using System.Text.Json;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;

namespace Booster.Core.Hags;

public enum HagsExperimentStage
{
    /// <summary>База снята; пользователь ещё не переключил HAGS или не перезагрузил компьютер.</summary>
    AwaitingSwitch,

    /// <summary>Новое значение действует; нужна серия-кандидат той же игры.</summary>
    AwaitingCandidate,

    Completed,
}

/// <summary>Файл state\hags-experiment.json, schemaVersion 1. Серии в него не копируются: только их Id.</summary>
/// <param name="StartedAt">Начало эксперимента; базовая серия снята раньше, но после последней загрузки.</param>
/// <param name="Target">Conditions.Target базовой серии: кандидат — той же игры.</param>
/// <param name="BaselineEnabled">Значение HAGS во время базовой серии.</param>
/// <param name="SwitchedBootTime">Загрузка, после которой Booster увидел новое значение.</param>
/// <param name="UnchangedSeenAt">
/// Когда Booster последний раз видел прежнее значение после перезагрузки (сначала — <paramref name="StartedAt"/>). Переключение
/// засчитывается только с загрузки позже этого времени: иначе его сделали после той загрузки, и оно ещё не действует.
/// </param>
public sealed record HagsExperiment(int SchemaVersion, DateTimeOffset StartedAt, string Target, string BaselineSeriesId,
    bool BaselineEnabled, HagsExperimentStage Stage, DateTimeOffset? SwitchedBootTime, string? CandidateSeriesId, DateTimeOffset UnchangedSeenAt)
{
    public const int CurrentSchemaVersion = 1;
}

public enum HagsCheck
{
    NotStarted,

    /// <summary>Файл испорчен или новой схемы; поможет только «Сбросить».</summary>
    Unreadable,

    /// <summary>Значение HAGS или время загрузки неизвестно: вывода нет.</summary>
    StateUnknown,

    WaitingForSwitch,

    /// <summary>Значение в реестре уже другое, но загрузки после переключения не было: оно ещё не действует.</summary>
    NeedsRestart,

    ReadyForCandidate,

    /// <summary>До серии-кандидата HAGS вернули к значению базы.</summary>
    SwitchedBack,

    /// <summary>Базовую серию удалили, например при очистке истории.</summary>
    BaselineMissing,

    CandidateMissing,

    Completed,
}

/// <summary>Ручная рекомендация по этим двум сериям; Booster HAGS не переключает.</summary>
public enum HagsVerdict
{
    Keep,
    Revert,
    NoDifference,
    Inconclusive,
}

public enum HagsStartRefusal
{
    AlreadyStarted,

    /// <summary>Значение HAGS, время его изменения или время загрузки неизвестно.</summary>
    StateUnknown,

    /// <summary>HAGS переключили после последней загрузки: значение в реестре может ещё не действовать.</summary>
    NeedsRestart,

    NoCompletedSeries,

    /// <summary>Новейшая завершённая серия снята до последней загрузки: каким было HAGS во время неё, не известно.</summary>
    SeriesBeforeBoot,
}

public enum HagsCandidateRefusal
{
    NotReady,
    SwitchedBack,

    /// <summary>Нет завершённой серии той же игры после загрузки с новым значением.</summary>
    NoCandidateSeries,
}

/// <param name="Current">Значение HAGS сейчас.</param>
/// <param name="Comparison">Только для <see cref="HagsCheck.Completed"/>; пересчитывается при каждом чтении.</param>
public sealed record HagsExperimentView(HagsExperiment? Experiment, HagsCheck Check, Reading<bool> Current,
    BenchmarkComparison? Comparison, HagsVerdict? Verdict);

public static class HagsVerdicts
{
    /// <summary>По среднему FPS и 1% low: противоречие или «не доказано» хотя бы в одном — вывода нет.</summary>
    public static HagsVerdict From(BenchmarkComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ChangeVerdict[] verdicts = [comparison.AverageFpsVerdict, comparison.OnePercentLowVerdict];
        if (!comparison.Comparable || verdicts.Contains(ChangeVerdict.Inconclusive))
            return HagsVerdict.Inconclusive;

        return (verdicts.Contains(ChangeVerdict.Improved), verdicts.Contains(ChangeVerdict.Worsened)) switch
        {
            (true, true) => HagsVerdict.Inconclusive,
            (_, true) => HagsVerdict.Revert,
            (true, _) => HagsVerdict.Keep,
            _ => HagsVerdict.NoDifference,
        };
    }
}

/// <summary>Формат файла — как у истории серий (<see cref="BenchmarkJson"/>): camelCase, перечисления строками.</summary>
internal static class HagsExperimentJson
{
    public static string Serialize(HagsExperiment experiment) => JsonSerializer.Serialize(experiment, BenchmarkJson.Options);

    /// <returns>null — файл испорчен, неполон, противоречив или новой схемы.</returns>
    public static HagsExperiment? TryDeserialize(string json)
    {
        HagsExperiment? experiment;
        try
        {
            experiment = JsonSerializer.Deserialize<HagsExperiment>(json, BenchmarkJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }

        return experiment is { SchemaVersion: HagsExperiment.CurrentSchemaVersion } && IsConsistent(experiment) ? experiment : null;
    }

    private static bool IsConsistent(HagsExperiment experiment) =>
        !string.IsNullOrWhiteSpace(experiment.Target)
        && !string.IsNullOrWhiteSpace(experiment.BaselineSeriesId)
        && Enum.IsDefined(experiment.Stage)
        && (experiment.Stage == HagsExperimentStage.AwaitingSwitch || experiment.SwitchedBootTime is not null)
        && (experiment.Stage != HagsExperimentStage.Completed || !string.IsNullOrWhiteSpace(experiment.CandidateSeriesId));
}
