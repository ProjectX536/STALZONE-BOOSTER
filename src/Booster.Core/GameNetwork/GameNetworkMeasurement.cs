using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.SystemHealth;

namespace Booster.Core.GameNetwork;

public enum ServerSource { Etw, TcpTable, Manual }

public sealed record ServerCandidate(string Address, double? EventsPerSecond);

public sealed record MeasuredServer(string Address, ServerSource Source, IReadOnlyList<ServerCandidate> Candidates);

/// <param name="SamplesMs">Индекс равен номеру раунда; null — нет ответа.</param>
public sealed record TargetSeries(NetworkTargetKind Kind, string Address, IReadOnlyList<double?> SamplesMs);

public enum HopStatus { TtlExpired, Destination, Timeout, Unreachable }

public sealed record RouteHop(int Ttl, string? Address, AddressScope? Scope, IReadOnlyList<double?> ProbesMs);

public sealed record RouteTrace(string Target, IReadOnlyList<RouteHop> Hops, bool ReachedTarget);

public sealed record WifiStatus(Reading<int> RssiDbm, Reading<int> Channel, Reading<WifiBand> Band,
    Reading<bool> BackgroundScanEnabled, Reading<bool> StreamingMode);

public sealed record ConsumerTraffic(int ProcessId, string Name, IReadOnlyList<string> Services, NetworkConsumerKind? Kind,
    double KBytesPerSecond);

public enum MeasurementEnd { Completed, GameExited }

public sealed record GameNetworkMeasurement(int SchemaVersion, string Id, DateTimeOffset StartedAt, TimeSpan Interval,
    string? Game, string? Label, MeasuredServer Server, NetworkAdapterInfo? Adapter, bool ViaTunnel,
    IReadOnlyList<TargetSeries> Series, RouteTrace? Route, Reading<WifiStatus> Wifi,
    Reading<IReadOnlyList<ConsumerTraffic>> Consumers, IReadOnlyList<string> ActiveActions,
    Reading<double> BoosterCpuPercent, MeasurementEnd End)
{
    public const int CurrentSchemaVersion = 1;
}
