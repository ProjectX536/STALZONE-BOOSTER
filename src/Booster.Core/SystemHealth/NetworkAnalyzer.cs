using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Reports;

namespace Booster.Core.SystemHealth;

public enum NetworkTargetKind
{
    Gateway,
    Host,
    IspHop,
    LastHop,
}

public sealed record NetworkTargetResult(NetworkTargetKind Kind, string Address, int Sent, int Received,
    Reading<double> MedianMs, Reading<double> JitterMs, Reading<double> LossPercent);

public sealed record NetworkTestResult(DateTimeOffset StartedAt, TimeSpan Duration, NetworkAdapterInfo? Adapter,
    IReadOnlyList<NetworkTargetResult> Targets, Reading<double> AdapterUtilizationPercent);

public static class NetworkAnalyzer
{
    public const string Source = "ICMP echo";
    public const string NoRepliesNote = "no replies: the host may not answer ICMP";

    public static NetworkTargetResult Summarize(NetworkTargetKind kind, string address, IReadOnlyList<double?> replies)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(replies);

        var received = replies.OfType<double>().ToArray();
        if (received.Length == 0)
        {
            // Многие роутеры просто не отвечают на ICMP, это не потери.
            var unknown = Reading<double>.Unknown(Source, NoRepliesNote);
            return new NetworkTargetResult(kind, address, replies.Count, 0, unknown, unknown, unknown);
        }

        var jitter = received.Length < 2
            ? Reading<double>.Unknown(Source, "jitter needs two replies")
            : Reading<double>.Known(received.Zip(received.Skip(1), (a, b) => Math.Abs(b - a)).Average(), Source);
        var loss = (double)(replies.Count - received.Length) / replies.Count * 100;
        return new NetworkTargetResult(kind, address, replies.Count, received.Length, Reading<double>.Known(Statistics.Median(received), Source),
            jitter, Reading<double>.Known(loss, Source));
    }

    public static IReadOnlyList<SystemAdvice> Advise(NetworkTestResult result, NetworkThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(thresholds);

        var advice = new List<SystemAdvice>();
        foreach (var target in result.Targets)
        {
            var latencyLimit = target.Kind == NetworkTargetKind.Gateway ? thresholds.GatewayLatencyMs : thresholds.HostLatencyMs;
            if (target.MedianMs.IsAvailable && target.MedianMs.Value >= latencyLimit)
                advice.Add(Warning(AdviceId.NetworkHighLatency, target.Address, Whole(target.MedianMs.Value)));
            if (target.JitterMs.IsAvailable && target.JitterMs.Value >= thresholds.JitterMs)
                advice.Add(Warning(AdviceId.NetworkJitter, target.Address, Whole(target.JitterMs.Value)));
            if (target.Received > 0 && target.LossPercent.IsAvailable && target.LossPercent.Value >= thresholds.LossPercent)
                advice.Add(Warning(AdviceId.NetworkLoss, target.Address, Whole(target.LossPercent.Value)));
        }

        if (advice.Count > 0 && result.Adapter?.Medium == NetworkMedium.Wireless)
            advice.Add(new SystemAdvice(AdviceId.NetworkWireless, AdviceModule.Network, FindingSeverity.Info, SafetyLevel.L3ManualOnly, []));
        return advice;
    }

    private static string Whole(double value) =>
        Math.Round(value, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);

    private static SystemAdvice Warning(AdviceId id, params string[] arguments) =>
        new(id, AdviceModule.Network, FindingSeverity.Warning, SafetyLevel.L3ManualOnly, arguments);
}
