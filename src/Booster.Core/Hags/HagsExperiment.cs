using System.Text.Json;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;

namespace Booster.Core.Hags;

public enum HagsExperimentStage
{
    AwaitingSwitch,

    AwaitingCandidate,

    Completed,
}

public sealed record HagsExperiment(int SchemaVersion, DateTimeOffset StartedAt, string Target, string BaselineSeriesId,
    bool BaselineEnabled, HagsExperimentStage Stage, DateTimeOffset? SwitchedBootTime, string? CandidateSeriesId, DateTimeOffset UnchangedSeenAt)
{
    public const int CurrentSchemaVersion = 1;
}

public enum HagsCheck
{
    NotStarted,

    Unreadable,

    StateUnknown,

    WaitingForSwitch,

    NeedsRestart,

    ReadyForCandidate,

    SwitchedBack,

    BaselineMissing,

    CandidateMissing,

    Completed,
}

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

    StateUnknown,

    NeedsRestart,

    NoCompletedSeries,

    SeriesBeforeBoot,
}

public enum HagsCandidateRefusal
{
    NotReady,
    SwitchedBack,

    NoCandidateSeries,
}

public sealed record HagsExperimentView(HagsExperiment? Experiment, HagsCheck Check, Reading<bool> Current,
    BenchmarkComparison? Comparison, HagsVerdict? Verdict);

public static class HagsVerdicts
{
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

internal static class HagsExperimentJson
{
    public static string Serialize(HagsExperiment experiment) => JsonSerializer.Serialize(experiment, BenchmarkJson.Options);

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
