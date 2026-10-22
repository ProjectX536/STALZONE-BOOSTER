using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Reports;
using Booster.Core.SystemHealth;

namespace Booster.Core.GameNetwork;

public static class GameNetworkAdvisor
{
    public static IReadOnlyList<SystemAdvice> Advise(GameNetworkMeasurement m, LatencyBreakdown breakdown, SpikeReport spikes,
        RouteJump? jump, GameNetworkThresholds thresholds, NetworkThresholds network)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(breakdown);
        ArgumentNullException.ThrowIfNull(spikes);
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentNullException.ThrowIfNull(network);

        var advice = new List<SystemAdvice>();
        var wireless = m.Adapter?.Medium == NetworkMedium.Wireless;

        var wifiScan = wireless && spikes.Gateway is { PeriodSeconds.IsAvailable: true };
        if (wifiScan)
            advice.Add(Make(AdviceId.GameNetworkWifiScanSpikes, FindingSeverity.Warning, Whole(spikes.Gateway!.PeriodSeconds.Value)));

        var periodicEnd = spikes.Gateway is not null && spikes.End.Count >= thresholds.PeriodicMinSpikes;
        if (!wifiScan && periodicEnd && spikes.AtHome * 2 >= spikes.End.Count)
            advice.Add(Make(AdviceId.GameNetworkLocalSpikes, FindingSeverity.Warning, Count(spikes.AtHome), Count(spikes.End.Count)));
        if (periodicEnd && spikes.BeyondGateway * 2 > spikes.End.Count)
            advice.Add(Make(AdviceId.GameNetworkRemoteSpikes, FindingSeverity.Warning, Count(spikes.BeyondGateway), Count(spikes.End.Count)));
        var spikeAdvice = advice.Count > 0;

        if (jump is not null)
            advice.Add(Make(AdviceId.GameNetworkRouteJump, FindingSeverity.Info, Count(jump.AtTtl), Whole(jump.IncreaseMs)));
        if (breakdown is { EndKind: NetworkTargetKind.Host, TotalMs: { IsAvailable: true, Value: var total } } && total >= network.HostLatencyMs)
            advice.Add(Make(AdviceId.GameNetworkFarServer, FindingSeverity.Info, Whole(total)));
        if (m.ViaTunnel)
            advice.Add(Make(AdviceId.GameNetworkVpn, FindingSeverity.Warning));

        AdviseConsumers(m, thresholds, advice);

        var wifiAdvice = false;
        if (m.Wifi is { IsAvailable: true, Value: { } wifi })
        {
            if (wifi.Band is { IsAvailable: true, Value: WifiBand.Band24GHz } && wifi.Channel.IsAvailable)
            {
                advice.Add(Make(AdviceId.GameNetworkWifi24GHz, FindingSeverity.Info, Count(wifi.Channel.Value)));
                wifiAdvice = true;
            }
            if (wifi.RssiDbm.IsAvailable && wifi.RssiDbm.Value < thresholds.WifiWeakSignalDbm)
            {
                advice.Add(Make(AdviceId.GameNetworkWifiWeakSignal, FindingSeverity.Warning, Count(wifi.RssiDbm.Value)));
                wifiAdvice = true;
            }
        }

        if (wireless && (spikeAdvice || wifiAdvice))
            advice.Add(Make(AdviceId.GameNetworkWireless, FindingSeverity.Info));
        return advice;
    }

    public static IReadOnlyList<SystemAdvice> AdviseLoad(LoadTestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result is { Outcome: LoadTestOutcome.Graded, Grade: >= BufferbloatGrade.C, IdleMedianMs.IsAvailable: true, LoadedMedianMs.IsAvailable: true })
            return [Make(AdviceId.GameNetworkBufferbloat, FindingSeverity.Warning, Whole(result.LoadedMedianMs.Value - result.IdleMedianMs.Value))];
        return [];
    }

    private static void AdviseConsumers(GameNetworkMeasurement m, GameNetworkThresholds thresholds, List<SystemAdvice> advice)
    {
        if (m.Consumers is not { IsAvailable: true, Value: { } consumers })
            return;

        var heavy = consumers.Where(c => c.KBytesPerSecond >= thresholds.BackgroundTrafficKBps).ToArray();
        foreach (var consumer in heavy.Where(c => c.Kind is not (NetworkConsumerKind.DeliveryOptimization or NetworkConsumerKind.WindowsUpdate)))
            advice.Add(Make(AdviceId.GameNetworkBackgroundTraffic, FindingSeverity.Warning, consumer.Name, Whole(consumer.KBytesPerSecond)));

        AddLargest(heavy, NetworkConsumerKind.DeliveryOptimization, AdviceId.GameNetworkDeliveryOptimization, advice);
        AddLargest(heavy, NetworkConsumerKind.WindowsUpdate, AdviceId.GameNetworkWindowsUpdate, advice);
    }

    private static void AddLargest(IEnumerable<ConsumerTraffic> heavy, NetworkConsumerKind kind, AdviceId id, List<SystemAdvice> advice)
    {
        var largest = heavy.Where(c => c.Kind == kind).MaxBy(c => c.KBytesPerSecond);
        if (largest is not null)
            advice.Add(Make(id, FindingSeverity.Warning, Whole(largest.KBytesPerSecond)));
    }

    private static string Whole(double value) =>
        Math.Round(value, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static SystemAdvice Make(AdviceId id, FindingSeverity severity, params string[] arguments) =>
        new(id, AdviceModule.GameNetwork, severity, SafetyLevel.L3ManualOnly, arguments);
}
