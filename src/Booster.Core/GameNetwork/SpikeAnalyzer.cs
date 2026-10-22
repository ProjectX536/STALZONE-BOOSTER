using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.SystemHealth;

namespace Booster.Core.GameNetwork;

public sealed record SeriesSpikes(int Count, IReadOnlyList<int> EventStarts, Reading<double> PeriodSeconds);

public sealed record SpikeReport(SeriesSpikes? Gateway, SeriesSpikes End, int AtHome, int BeyondGateway);

public static class SpikeAnalyzer
{
    private const string Source = "ping spike intervals";
    private const int MinReplies = 3;

    public static IReadOnlyList<int> SpikeRounds(IReadOnlyList<double?> samples, double minDeltaMs, double madFactor)
    {
        var replied = samples.Where(s => s.HasValue).Select(s => s!.Value).ToArray();
        if (replied.Length < MinReplies)
            return [];

        var limit = Statistics.Median(replied) + Math.Max(minDeltaMs, madFactor * Statistics.Mad(replied));
        var rounds = new List<int>();
        for (var i = 0; i < samples.Count; i++)
        {
            if (samples[i] > limit)
                rounds.Add(i);
        }
        return rounds;
    }

    public static SeriesSpikes Analyze(IReadOnlyList<double?> samples, TimeSpan interval, GameNetworkThresholds thresholds)
    {
        var rounds = SpikeRounds(samples, thresholds.SpikeMinDeltaMs, thresholds.SpikeMadFactor);
        var starts = new List<int>();
        for (var i = 0; i < rounds.Count; i++)
        {
            if (i == 0 || rounds[i] != rounds[i - 1] + 1)
                starts.Add(rounds[i]);
        }
        return new SeriesSpikes(rounds.Count, starts, Period(starts, interval, thresholds));
    }

    public static SpikeReport Analyze(TargetSeries? gateway, TargetSeries end, TimeSpan interval, GameNetworkThresholds thresholds)
    {
        var gatewaySpikes = gateway is null ? null : Analyze(gateway.SamplesMs, interval, thresholds);
        var endSpikes = Analyze(end.SamplesMs, interval, thresholds);
        if (gateway is null)
            return new SpikeReport(null, endSpikes, 0, 0);

        var gatewayRounds = SpikeRounds(gateway.SamplesMs, thresholds.SpikeMinDeltaMs, thresholds.SpikeMadFactor).ToHashSet();
        var endRounds = SpikeRounds(end.SamplesMs, thresholds.SpikeMinDeltaMs, thresholds.SpikeMadFactor);
        var atHome = endRounds.Count(gatewayRounds.Contains);
        return new SpikeReport(gatewaySpikes, endSpikes, atHome, endRounds.Count - atHome);
    }

    private static Reading<double> Period(IReadOnlyList<int> starts, TimeSpan interval, GameNetworkThresholds thresholds)
    {
        if (starts.Count < thresholds.PeriodicMinSpikes)
            return Reading<double>.Unknown(Source, "not periodic");

        var gaps = new double[starts.Count - 1];
        for (var i = 0; i < gaps.Length; i++)
            gaps[i] = starts[i + 1] - starts[i];

        var mean = gaps.Average();
        var deviation = Math.Sqrt(gaps.Sum(g => (g - mean) * (g - mean)) / gaps.Length);
        if (mean <= 0 || deviation / mean > thresholds.PeriodicMaxVariation)
            return Reading<double>.Unknown(Source, "not periodic");

        return Reading<double>.Known(mean * interval.TotalSeconds, Source);
    }
}
