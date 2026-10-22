using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.SystemHealth;

namespace Booster.Core.GameNetwork;

public sealed record TargetAnalysis(NetworkTargetResult Summary, Reading<double> P95Ms);

public sealed record GameNetworkAnalysis(IReadOnlyList<TargetAnalysis> Targets, LatencyBreakdown Breakdown, SpikeReport Spikes,
    RouteJump? Jump, IReadOnlyList<SystemAdvice> Advice);

public static class GameNetworkAnalyzer
{
    /// <summary>Меньше 20 ответов — p95 совпадает с максимумом и ничего не говорит о хвосте.</summary>
    public const int MinimumRepliesForP95 = 20;

    private const string Source = "ping samples";

    public static GameNetworkAnalysis Analyze(GameNetworkMeasurement measurement, GameNetworkThresholds thresholds, NetworkThresholds network)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentNullException.ThrowIfNull(network);

        var series = measurement.Series;
        var targets = series.Select(s => new TargetAnalysis(NetworkAnalyzer.Summarize(s.Kind, s.Address, s.SamplesMs), P95(s.SamplesMs))).ToArray();

        var endIndex = IndexOf(series, NetworkTargetKind.Host);
        if (endIndex < 0)
            endIndex = IndexOf(series, NetworkTargetKind.LastHop);
        if (endIndex < 0)
            throw new ArgumentException("The measurement has no Host or LastHop series.", nameof(measurement));
        var gatewayIndex = IndexOf(series, NetworkTargetKind.Gateway);
        var ispIndex = IndexOf(series, NetworkTargetKind.IspHop);

        var breakdown = LatencyBreakdownCalculator.Compute(
            gatewayIndex < 0 ? null : targets[gatewayIndex].Summary,
            ispIndex < 0 ? null : targets[ispIndex].Summary,
            targets[endIndex].Summary);
        var spikes = SpikeAnalyzer.Analyze(gatewayIndex < 0 ? null : series[gatewayIndex], series[endIndex], measurement.Interval, thresholds);
        var jump = measurement.Route is null ? null : RouteAnalyzer.FindJump(measurement.Route, thresholds.RouteJumpMs);
        var advice = GameNetworkAdvisor.Advise(measurement, breakdown, spikes, jump, thresholds, network);

        return new GameNetworkAnalysis(targets, breakdown, spikes, jump, advice);
    }

    public static Reading<double> P95(IReadOnlyList<double?> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var replies = samples.OfType<double>().Order().ToArray();
        if (replies.Length < MinimumRepliesForP95)
            return Reading<double>.Unknown(Source, "too few replies for p95");

        // Ближайший ранг sorted[ceil(0,95·n) − 1] в целых числах: double округлил бы 0,95·100 непредсказуемо.
        var rank = (replies.Length * 95 + 99) / 100;
        return Reading<double>.Known(replies[rank - 1], Source);
    }

    private static int IndexOf(IReadOnlyList<TargetSeries> series, NetworkTargetKind kind)
    {
        for (var i = 0; i < series.Count; i++)
        {
            if (series[i].Kind == kind)
                return i;
        }
        return -1;
    }
}
