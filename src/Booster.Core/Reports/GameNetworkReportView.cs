using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.GameNetwork;
using Booster.Core.SystemHealth;

namespace Booster.Core.Reports;

public sealed record TargetView(NetworkTargetKind Kind, Reading<double> MedianMs, Reading<double> P95Ms, Reading<double> JitterMs,
    Reading<double> LossPercent);

public sealed record HopView(int Ttl, AddressScope? Scope, Reading<double> MedianMs);

public sealed record ConsumerView(string Name, NetworkConsumerKind? Kind, double KBytesPerSecond);

/// <summary>
/// Замер сети в игре для отчёта. Адресов узлов и шлюза здесь нет: по ним видно провайдера и город.
/// Из адресов остаётся только сервер игры.
/// </summary>
public sealed record GameNetworkReportView(DateTimeOffset StartedAt, string? Game, string Server, ServerSource ServerSource,
    NetworkMedium? Medium, long? LinkSpeedBitsPerSecond, bool ViaTunnel, IReadOnlyList<TargetView> Targets, LatencyBreakdown Breakdown,
    SpikeReport Spikes, IReadOnlyList<HopView> Route, RouteJump? Jump, Reading<WifiStatus> Wifi, Reading<IReadOnlyList<ConsumerView>> Consumers,
    LoadTestResult? Load, GameNetworkComparison? Comparison, Reading<double> BoosterCpuPercent, MeasurementEnd End,
    IReadOnlyList<SystemAdvice> Advice)
{
    public static GameNetworkReportView From(GameNetworkMeasurement m, GameNetworkAnalysis a, LoadTestResult? load,
        GameNetworkComparison? comparison)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(a);

        var targets = a.Targets
            .Select(t => new TargetView(t.Summary.Kind, t.Summary.MedianMs, t.P95Ms, t.Summary.JitterMs, t.Summary.LossPercent))
            .ToArray();
        var route = m.Route?.Hops.Select(h => new HopView(h.Ttl, h.Scope, RouteAnalyzer.HopMedian(h))).ToArray() ?? [];
        var advice = load is null ? a.Advice : [.. a.Advice, .. GameNetworkAdvisor.AdviseLoad(load)];

        return new GameNetworkReportView(m.StartedAt, m.Game, m.Server.Address, m.Server.Source, m.Adapter?.Medium,
            m.Adapter?.LinkSpeedBitsPerSecond, m.ViaTunnel, targets, a.Breakdown, a.Spikes, route, a.Jump, m.Wifi, ConsumerViews(m.Consumers),
            load, comparison, m.BoosterCpuPercent, m.End, advice);
    }

    private static Reading<IReadOnlyList<ConsumerView>> ConsumerViews(Reading<IReadOnlyList<ConsumerTraffic>> consumers)
    {
        IReadOnlyList<ConsumerView>? views = consumers is { IsAvailable: true, Value: { } list }
            ? list.Select(c => new ConsumerView(c.Name, c.Kind, c.KBytesPerSecond)).ToArray()
            : null;
        return new Reading<IReadOnlyList<ConsumerView>>(views, consumers.Status, consumers.Source, consumers.Note);
    }
}
