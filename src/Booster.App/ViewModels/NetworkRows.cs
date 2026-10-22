using System.Globalization;
using Booster.App.Resources;
using Booster.Core.Abstractions;
using Booster.Core.GameNetwork;
using Booster.Core.Reports;
using Booster.Core.Resources;
using Booster.Core.SystemHealth;
using Booster.Platform.Windows.Providers.GameNetwork;

namespace Booster.App.ViewModels;

public sealed record NetworkCandidateRow(string Address, string EventsPerSecond);

public sealed record NetworkTargetRow(string Target, string Address, ReportCell Median, ReportCell P95, ReportCell Jitter, ReportCell Loss);

public sealed record NetworkHopRow(string Ttl, string Address, string Scope, ReportCell Median);

public sealed record NetworkConsumerRow(string Name, string Kind, string Rate);

public sealed record NetworkAdviceRow(string Title, string Body, string Action, string? SettingsPage);

public sealed record NetworkMeasurementRow(string Id, string StartedAt, string Server, string Label, string End);

public sealed record MeasurementGroupRow(MeasurementGroup Group, string Title)
{
    public override string ToString() => Title;
}

/// <summary>Строки экрана «Сеть в игре». Адреса узлов и шлюза остаются здесь и в отчёт не попадают.</summary>
internal static class NetworkRows
{
    private const double BitsPerKilobit = 1e3;
    private const double BitsPerMegabit = 1e6;
    private const double BitsPerGigabit = 1e9;

    private static readonly Dictionary<AdviceId, string> SettingsPages = new()
    {
        [AdviceId.GameNetworkDeliveryOptimization] = "delivery-optimization",
        [AdviceId.GameNetworkWindowsUpdate] = "windowsupdate",
    };

    public static string Ms(double value, CultureInfo culture) => Format(culture, ReportStrings.UnitMs, value.ToString("0.#", culture));

    public static string Server(MeasuredServer server, CultureInfo culture) =>
        Format(culture, Strings.NetworkServerFormat, server.Address, EnumText.Format(server.Source));

    public static string? ServerNote(GameNetworkMeasurement measurement, GameNetworkAnalysis analysis)
    {
        List<string> notes = [];
        if (measurement.Server.Source == ServerSource.TcpTable)
            notes.Add(Strings.NetworkServerTcpTable);
        if (analysis.Breakdown.EndKind == NetworkTargetKind.LastHop)
            notes.Add(Strings.NetworkServerLastHop);
        if (measurement.ViaTunnel)
            notes.Add(Strings.NetworkServerViaTunnel);
        return notes.Count > 0 ? string.Join(Environment.NewLine, notes) : null;
    }

    public static IReadOnlyList<NetworkCandidateRow> Candidates(MeasuredServer server, CultureInfo culture) =>
    [
        .. server.Candidates
            .Where(c => !string.Equals(c.Address, server.Address, StringComparison.Ordinal))
            .Select(c => new NetworkCandidateRow(c.Address, c.EventsPerSecond is { } rate
                ? rate.ToString("0.#", culture)
                : ReadingFormatter.StatusText(DataStatus.Unknown))),
    ];

    public static IReadOnlyList<ReportField> Breakdown(LatencyBreakdown breakdown, CultureInfo culture) =>
    [
        new(ReportStrings.GameNetworkHome, SensorFieldFormatter.Value(breakdown.HomeMs, v => Ms(v, culture))),
        new(ReportStrings.GameNetworkLine, SensorFieldFormatter.Value(breakdown.LineMs, v => Ms(v, culture))),
        new(ReportStrings.GameNetworkRoute, SensorFieldFormatter.Value(breakdown.RouteMs, v => Ms(v, culture))),
        new(ReportStrings.GameNetworkTotal, SensorFieldFormatter.Value(breakdown.TotalMs, v => Ms(v, culture))),
    ];

    public static IReadOnlyList<NetworkTargetRow> Targets(GameNetworkAnalysis analysis, CultureInfo culture) =>
    [
        .. analysis.Targets.Select(t => new NetworkTargetRow(EnumText.Format(t.Summary.Kind), t.Summary.Address,
            SensorFieldFormatter.Value(t.Summary.MedianMs, v => Ms(v, culture)), SensorFieldFormatter.Value(t.P95Ms, v => Ms(v, culture)),
            SensorFieldFormatter.Value(t.Summary.JitterMs, v => Ms(v, culture)),
            SensorFieldFormatter.Value(t.Summary.LossPercent, v => SensorFieldFormatter.Percent(v, culture)))),
    ];

    public static IReadOnlyList<NetworkHopRow> Route(RouteTrace? route, CultureInfo culture) =>
    [
        .. (route?.Hops ?? []).Select(h => new NetworkHopRow(h.Ttl.ToString(culture), h.Address ?? ReportStrings.GameNetworkHopSilent,
            h.Scope is { } scope ? EnumText.Format(scope) : ReportStrings.GameNetworkHopSilent,
            SensorFieldFormatter.Value(RouteAnalyzer.HopMedian(h), v => Ms(v, culture)))),
    ];

    public static string? RouteNote(GameNetworkMeasurement measurement, GameNetworkAnalysis analysis, CultureInfo culture)
    {
        if (measurement.Route is null || measurement.Route.Hops.Count == 0)
            return ReportStrings.GameNetworkNoRoute;
        return analysis.Jump is { } jump
            ? Format(culture, ReportStrings.GameNetworkJumpFormat, jump.AfterTtl, jump.AtTtl, jump.IncreaseMs.ToString("0.#", culture))
            : null;
    }

    public static string Spikes(SpikeReport spikes, CultureInfo culture)
    {
        var period = spikes.End.PeriodSeconds.IsAvailable
            ? Format(culture, ReportStrings.GameNetworkSpikePeriodFormat, spikes.End.PeriodSeconds.Value.ToString("0.#", culture))
            : ReportStrings.GameNetworkSpikeNotPeriodic;
        var text = Format(culture, Strings.NetworkSpikesFormat, spikes.End.Count.ToString(culture), period);
        return spikes.Gateway is null
            ? text
            : text + Environment.NewLine + Format(culture, Strings.NetworkSpikesPlaceFormat, spikes.AtHome.ToString(culture),
                spikes.BeyondGateway.ToString(culture));
    }

    public static IReadOnlyList<ReportField> Wifi(GameNetworkMeasurement measurement, CultureInfo culture)
    {
        if (measurement.Wifi is not { IsAvailable: true, Value: { } wifi })
            return [];
        var speed = measurement.Adapter?.LinkSpeedBitsPerSecond is { } bits
            ? new ReportCell(Speed(bits, culture))
            : new ReportCell(ReadingFormatter.StatusText(DataStatus.Unknown), Tone: CellTone.Unavailable);
        return
        [
            new(ReportStrings.GameNetworkWifiSignal, SensorFieldFormatter.Value(wifi.RssiDbm, v => Format(culture, ReportStrings.GameNetworkDbmFormat, v))),
            new(ReportStrings.GameNetworkWifiChannel, SensorFieldFormatter.Value(wifi.Channel, v => v.ToString(culture))),
            new(ReportStrings.GameNetworkWifiBand, SensorFieldFormatter.Value(wifi.Band, b => EnumText.Format(b))),
            new(ReportStrings.GameNetworkLinkSpeed, speed),
            new(ReportStrings.GameNetworkWifiScan, SensorFieldFormatter.Value(wifi.BackgroundScanEnabled, Bool)),
            new(ReportStrings.GameNetworkWifiStreaming, SensorFieldFormatter.Value(wifi.StreamingMode, Bool)),
        ];
    }

    public static string? WifiText(GameNetworkMeasurement measurement, CultureInfo culture)
    {
        if (measurement.Wifi.IsAvailable)
            return null;
        return measurement.Adapter?.Medium == NetworkMedium.Ethernet
            ? ReportStrings.GameNetworkCable
            : Format(culture, Strings.NetworkWifiUnavailableFormat, ReadingFormatter.Format(measurement.Wifi, _ => string.Empty));
    }

    /// <summary>Windows 11 отдаёт сигнал и диапазон Wi-Fi только приложениям с доступом к местоположению.</summary>
    public static bool LocationDenied(Reading<WifiStatus> wifi)
    {
        if (IsDenied(wifi.Note))
            return true;
        return wifi.Value is { } status && (IsDenied(status.RssiDbm.Note) || IsDenied(status.Channel.Note) || IsDenied(status.Band.Note)
            || IsDenied(status.BackgroundScanEnabled.Note) || IsDenied(status.StreamingMode.Note));
    }

    public static IReadOnlyList<NetworkConsumerRow> Consumers(Reading<IReadOnlyList<ConsumerTraffic>> consumers, CultureInfo culture) =>
        consumers is { IsAvailable: true, Value: { } list }
            ? [.. list.Select(c => new NetworkConsumerRow(c.Name, c.Kind is { } kind ? EnumText.Format(kind) : ReportStrings.GameNetworkConsumerUnclassified,
                c.KBytesPerSecond.ToString("0.#", culture)))]
            : [];

    public static string? ConsumersText(Reading<IReadOnlyList<ConsumerTraffic>> consumers) =>
        consumers.IsAvailable ? null : ReadingFormatter.Format(consumers, _ => string.Empty);

    public static IReadOnlyList<NetworkAdviceRow> Advice(IReadOnlyList<SystemAdvice> advice, CultureInfo culture) =>
    [
        .. advice.Select(a => new NetworkAdviceRow(AdviceText.Title(a), AdviceText.Body(a, culture), AdviceText.Action(a),
            SettingsPages.GetValueOrDefault(a.Id))),
    ];

    public static IReadOnlyList<ReportField> Load(LoadTestResult load, CultureInfo culture)
    {
        List<ReportField> fields =
        [
            new(ReportStrings.GameNetworkLoadTarget, new ReportCell(EnumText.Format(load.TargetKind))),
            new(ReportStrings.GameNetworkLoadIdle, SensorFieldFormatter.Value(load.IdleMedianMs, v => Ms(v, culture))),
            new(ReportStrings.GameNetworkLoadLoaded, SensorFieldFormatter.Value(load.LoadedMedianMs, v => Ms(v, culture))),
            new(ReportStrings.GameNetworkLoadThroughput, SensorFieldFormatter.Value(load.ThroughputMbps,
                v => Format(culture, ReportStrings.UnitMbps, v.ToString("0.#", culture)))),
            new(ReportStrings.GameNetworkLoadOutcome, new ReportCell(EnumText.Format(load.Outcome))),
        ];
        if (load.Grade is { } grade)
        {
            fields.Add(new(ReportStrings.GameNetworkLoadGrade, new ReportCell(EnumText.Format(grade),
                Tone: grade >= BufferbloatGrade.C ? CellTone.Warning : CellTone.Normal)));
        }
        return fields;
    }

    public static MeasurementGroupRow Group(MeasurementGroup group, CultureInfo culture)
    {
        var title = Format(culture, Strings.NetworkGroupFormat, group.Label ?? Strings.NetworkGroupNoLabel, group.Server,
            group.Runs.Count.ToString(culture));
        if (group.ActiveActions.Count > 0)
            title = Format(culture, Strings.NetworkGroupActionsFormat, title, string.Join(", ", group.ActiveActions));
        return new MeasurementGroupRow(group, title);
    }

    public static string Comparison(GameNetworkComparison? comparison, bool sameGroup, CultureInfo culture)
    {
        if (comparison is null)
            return sameGroup ? Strings.NetworkComparisonSame : Strings.NetworkComparisonNone;

        List<string> lines = [Format(culture, Strings.NetworkVerdictFormat, EnumText.Format(comparison.Verdict))];
        lines.AddRange(comparison.Metrics.Select(m => Format(culture, Strings.NetworkMetricFormat, EnumText.Format(m.Metric),
            MetricValue(m.Metric, m.Baseline, culture), MetricValue(m.Metric, m.Candidate, culture), MetricChange(m.Metric, m.Change, culture))));
        lines.AddRange(comparison.Caveats.Select(c => EnumText.Format(c)));
        lines.Add(Strings.NetworkComparisonCaveat);
        return string.Join(Environment.NewLine, lines);
    }

    public static NetworkMeasurementRow Measurement(GameNetworkMeasurement m, CultureInfo culture) => new(m.Id,
        m.StartedAt.ToLocalTime().ToString("g", culture), m.Server.Address, m.Label ?? Strings.NetworkGroupNoLabel, EnumText.Format(m.End));

    private static string MetricValue(NetworkMetric metric, double value, CultureInfo culture) => metric switch
    {
        NetworkMetric.LossPercent => SensorFieldFormatter.Percent(value, culture),
        NetworkMetric.SpikesPerMinute => Format(culture, ReportStrings.GameNetworkPerMinuteFormat, value.ToString("0.#", culture)),
        _ => Ms(value, culture),
    };

    private static string MetricChange(NetworkMetric metric, double change, CultureInfo culture)
    {
        var signed = change.ToString("+0.#;-0.#;0", culture);
        return metric switch
        {
            NetworkMetric.LossPercent => Format(culture, ReportStrings.GameNetworkPointsFormat, signed),
            NetworkMetric.SpikesPerMinute => Format(culture, ReportStrings.GameNetworkPerMinuteFormat, signed),
            _ => Format(culture, ReportStrings.UnitMs, signed),
        };
    }

    private static string Speed(long bitsPerSecond, CultureInfo culture) => bitsPerSecond switch
    {
        >= (long)BitsPerGigabit => Format(culture, ReportStrings.UnitGbps, (bitsPerSecond / BitsPerGigabit).ToString("0.#", culture)),
        >= (long)BitsPerMegabit => Format(culture, ReportStrings.UnitMbps, (bitsPerSecond / BitsPerMegabit).ToString("0.#", culture)),
        _ => Format(culture, ReportStrings.UnitKbps, (bitsPerSecond / BitsPerKilobit).ToString("0.#", culture)),
    };

    private static bool IsDenied(string? note) => string.Equals(note, WifiSource.AccessDeniedNote, StringComparison.Ordinal);

    private static string Bool(bool value) => value ? ReportStrings.Yes : ReportStrings.No;

    private static string Format(CultureInfo culture, string format, params object[] args) => string.Format(culture, format, args);
}
