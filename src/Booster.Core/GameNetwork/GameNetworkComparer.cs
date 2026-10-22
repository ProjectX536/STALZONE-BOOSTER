using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;

namespace Booster.Core.GameNetwork;

public enum NetworkMetric { MedianMs, P95Ms, JitterMs, LossPercent, SpikesPerMinute }

/// <param name="Change">Кандидат минус база: для задержки в мс, для потерь в процентных пунктах, для скачков в штуках в минуту.</param>
public sealed record NetworkMetricComparison(NetworkMetric Metric, double Baseline, double Candidate, double Change, ChangeVerdict Verdict);

public sealed record MeasurementGroup(string Server, string? Label, IReadOnlyList<string> ActiveActions,
    IReadOnlyList<GameNetworkMeasurement> Runs);

public sealed record GameNetworkComparison(string Server, int BaselineRuns, int CandidateRuns, IReadOnlyList<NetworkMetricComparison> Metrics,
    IReadOnlyList<ComparisonCaveat> Caveats, ChangeVerdict Verdict);

public static class GameNetworkComparer
{
    private const char KeySeparator = '\u001f';

    public static IReadOnlyList<MeasurementGroup> Group(IReadOnlyList<GameNetworkMeasurement> all)
    {
        ArgumentNullException.ThrowIfNull(all);

        return all
            .Where(m => m.End == MeasurementEnd.Completed)
            .GroupBy(Key)
            .Select(g =>
            {
                var runs = g.OrderBy(m => m.StartedAt).ToArray();
                return new MeasurementGroup(runs[0].Server.Address, runs[0].Label, Actions(runs[0]), runs);
            })
            .OrderByDescending(g => g.Runs[^1].StartedAt)
            .ToArray();
    }

    public static GameNetworkComparison Compare(MeasurementGroup baseline, MeasurementGroup candidate,
        GameNetworkThresholds thresholds, NetworkThresholds network)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentNullException.ThrowIfNull(network);

        var caveats = new List<ComparisonCaveat>();
        if (!string.Equals(baseline.Server, candidate.Server, StringComparison.Ordinal) || MediumsDiffer(baseline, candidate))
            caveats.Add(ComparisonCaveat.ConditionsDiffer);

        var baseValues = Values(baseline, thresholds, network);
        var candValues = Values(candidate, thresholds, network);

        var metrics = new List<NetworkMetricComparison>();
        foreach (var metric in Enum.GetValues<NetworkMetric>())
        {
            var b = baseValues[metric];
            var c = candValues[metric];
            if (b.Count == 0 || c.Count == 0)
            {
                AddCaveat(caveats, ComparisonCaveat.TooFewRuns);
                continue;
            }

            var comparison = CompareMetric(metric, b, c, thresholds, out var tooFew, out var variation);
            metrics.Add(comparison);
            if (tooFew)
                AddCaveat(caveats, ComparisonCaveat.TooFewRuns);
            if (variation)
                AddCaveat(caveats, ComparisonCaveat.HighRunVariation);
        }

        var verdict = caveats.Contains(ComparisonCaveat.TooFewRuns) || caveats.Contains(ComparisonCaveat.ConditionsDiffer)
            ? ChangeVerdict.Inconclusive
            : Overall(metrics);
        return new GameNetworkComparison(baseline.Server, baseline.Runs.Count, candidate.Runs.Count, metrics, caveats, verdict);
    }

    private static string Key(GameNetworkMeasurement m) =>
        string.Join(KeySeparator, [m.Server.Address, m.Label ?? "", .. Actions(m)]);

    private static string[] Actions(GameNetworkMeasurement m) => [.. m.ActiveActions.Order(StringComparer.Ordinal)];

    private static bool MediumsDiffer(MeasurementGroup baseline, MeasurementGroup candidate) =>
        baseline.Runs.Concat(candidate.Runs).Select(r => r.Adapter?.Medium).Distinct().Count() > 1;

    private static void AddCaveat(List<ComparisonCaveat> caveats, ComparisonCaveat caveat)
    {
        if (!caveats.Contains(caveat))
            caveats.Add(caveat);
    }

    private static Dictionary<NetworkMetric, List<double>> Values(MeasurementGroup group, GameNetworkThresholds thresholds, NetworkThresholds network)
    {
        var values = Enum.GetValues<NetworkMetric>().ToDictionary(m => m, _ => new List<double>());
        foreach (var run in group.Runs)
        {
            var analysis = GameNetworkAnalyzer.Analyze(run, thresholds, network);
            var end = analysis.Targets.First(t => t.Summary.Kind == analysis.Breakdown.EndKind);

            Add(values[NetworkMetric.MedianMs], end.Summary.MedianMs);
            Add(values[NetworkMetric.P95Ms], end.P95Ms);
            Add(values[NetworkMetric.JitterMs], end.Summary.JitterMs);
            Add(values[NetworkMetric.LossPercent], end.Summary.LossPercent);

            var endSeries = run.Series.First(s => s.Kind == analysis.Breakdown.EndKind).SamplesMs;
            var minutes = endSeries.Count * run.Interval.TotalMinutes;
            if (minutes > 0 && endSeries.Any(s => s is not null))
                values[NetworkMetric.SpikesPerMinute].Add(analysis.Spikes.End.Count / minutes);
        }
        return values;
    }

    private static void Add(List<double> list, Reading<double> reading)
    {
        if (reading.IsAvailable)
            list.Add(reading.Value);
    }

    private static NetworkMetricComparison CompareMetric(NetworkMetric metric, List<double> b, List<double> c,
        GameNetworkThresholds thresholds, out bool tooFew, out bool variation)
    {
        tooFew = b.Count < thresholds.ComparisonMinimumRuns || c.Count < thresholds.ComparisonMinimumRuns;
        variation = false;

        var baseMean = b.Average();
        var candMean = c.Average();
        var change = candMean - baseMean;
        var result = (ChangeVerdict verdict) => new NetworkMetricComparison(metric, baseMean, candMean, change, verdict);

        if (tooFew)
            return result(ChangeVerdict.Inconclusive);

        var isMs = metric is NetworkMetric.MedianMs or NetworkMetric.P95Ms or NetworkMetric.JitterMs;
        // Нулевая база при ненулевом изменении: процентное изменение бесконечно и порог проходит.
        var percent = change == 0 ? 0 : baseMean == 0 ? double.PositiveInfinity : Math.Abs(change) / Math.Abs(baseMean) * 100;
        if (percent < thresholds.ComparisonMinimumChangePercent || (isMs && Math.Abs(change) < thresholds.ComparisonMinimumChangeMs))
            return result(ChangeVerdict.NoChange);

        if (c.Max() < b.Min())
            return result(ChangeVerdict.Improved);
        if (c.Min() > b.Max())
            return result(ChangeVerdict.Worsened);

        variation = true;
        return result(ChangeVerdict.Inconclusive);
    }

    private static ChangeVerdict Overall(IReadOnlyList<NetworkMetricComparison> metrics)
    {
        var improved = metrics.Any(m => m.Verdict == ChangeVerdict.Improved);
        var worsened = metrics.Any(m => m.Verdict == ChangeVerdict.Worsened);
        if (improved && !worsened)
            return ChangeVerdict.Improved;
        if (worsened && !improved)
            return ChangeVerdict.Worsened;
        if (metrics.Count > 0 && metrics.All(m => m.Verdict == ChangeVerdict.NoChange))
            return ChangeVerdict.NoChange;
        return ChangeVerdict.Inconclusive;
    }
}
