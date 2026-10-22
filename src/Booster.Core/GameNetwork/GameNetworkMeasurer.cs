using System.Globalization;
using System.Net;
using System.Text;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Profiles;
using Booster.Core.SystemHealth;
using Microsoft.Extensions.Logging;

namespace Booster.Core.GameNetwork;

public sealed record GameNetworkRequest(string? ManualServer, string? Label);

public enum GameNetworkPhase { FindingGame, DetectingServer, Tracing, Measuring, Saving }

public sealed record GameNetworkProgress(GameNetworkPhase Phase, int Done, int Total);

public enum GameNetworkRunStatus { Saved, NoGame, NoServer, InvalidManualServer }

public sealed record GameNetworkRunResult(GameNetworkRunStatus Status, GameNetworkMeasurement? Measurement, GameNetworkAnalysis? Analysis);

public interface IGameNetworkMeasurer
{
    Task<GameNetworkRunResult> MeasureAsync(GameNetworkRequest request, IProgress<GameNetworkProgress>? progress, CancellationToken ct);
}

public sealed partial class GameNetworkMeasurer(IProcessProvider processes, INetworkAdapterSource adapters, IRouteSource routes,
    IWifiSource wifi, ISelfCpuSource selfCpu, IActiveSessionActions activeActions, IGameNetworkStore store, ServerResolver resolver,
    TraceRouteRunner trace, PingRoundRunner pingRounds, ConsumerResolver consumers, IProfileRepository profiles,
    IBoosterConfiguration config, TimeProvider time, ILogger<GameNetworkMeasurer> logger) : IGameNetworkMeasurer
{
    public const string WifiSource = "Wi-Fi";
    public const string TrafficSource = "ETW traffic";
    public const string SelfCpuSource = "Booster CPU";

    private const string InvalidFileNameChars = "<>:\"/\\|?*";

    private int _running;

    private sealed class RoundProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    public async Task<GameNetworkRunResult> MeasureAsync(GameNetworkRequest request, IProgress<GameNetworkProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            throw new InvalidOperationException("A game network measurement is already running.");
        try
        {
            return await MeasureCoreAsync(request, progress, ct).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private async Task<GameNetworkRunResult> MeasureCoreAsync(GameNetworkRequest request, IProgress<GameNetworkProgress>? progress, CancellationToken ct)
    {

        var manual = string.IsNullOrWhiteSpace(request.ManualServer) ? null : request.ManualServer.Trim();
        if (manual is not null)
        {
            if (!IPAddress.TryParse(manual, out var manualAddress) || !AddressClassifier.IsPublic(manualAddress) || IsShortIpv4(manual))
                return new GameNetworkRunResult(GameNetworkRunStatus.InvalidManualServer, null, null);
            manual = manualAddress.ToString();
        }

        progress?.Report(new GameNetworkProgress(GameNetworkPhase.FindingGame, 0, 0));
        var game = ProfileMatcher.MatchAll(processes.List(), profiles.Current.Profiles).FirstOrDefault();
        if (game is null && manual is null)
            return new GameNetworkRunResult(GameNetworkRunStatus.NoGame, null, null);

        progress?.Report(new GameNetworkProgress(GameNetworkPhase.DetectingServer, 0, 0));
        var gameIds = game?.ProcessIds.ToHashSet() ?? [];
        var identities = gameIds.Select(id => (ProcessId: id, Identity: processes.Identify(id))).ToArray();
        var sessionStarted = time.GetTimestamp();
        var resolved = await resolver.ResolveAsync(manual, gameIds, ct).ConfigureAwait(false);
        if (resolved is null)
            return new GameNetworkRunResult(GameNetworkRunStatus.NoServer, null, null);

        try
        {
            return await MeasureServerAsync(request, progress, game, gameIds, identities, resolved, sessionStarted, ct).ConfigureAwait(false);
        }
        finally
        {
            resolved.Session?.Dispose();
        }
    }

    private async Task<GameNetworkRunResult> MeasureServerAsync(GameNetworkRequest request, IProgress<GameNetworkProgress>? progress,
        GameMatch? game, IReadOnlySet<int> gameIds, IReadOnlyList<(int ProcessId, ProcessIdentity? Identity)> identities, ResolvedServer resolved,
        long sessionStarted, CancellationToken ct)
    {
        var settings = config.Current.Settings;
        var server = resolved.Server;
        var serverAddress = IPAddress.Parse(server.Address);

        var adapter = FindAdapter(serverAddress);
        var viaTunnel = adapter is not null && TunnelDetector.IsTunnel(adapter, config.Current.Catalog.TunnelKeywords);
        var gateway = adapter is null ? null : FirstGatewayOf(adapter, serverAddress.AddressFamily);

        progress?.Report(new GameNetworkProgress(GameNetworkPhase.Tracing, 0, 0));
        var route = await TraceAsync(serverAddress, ct).ConfigureAwait(false);
        var end = EndTarget(route, server.Address);
        var ispHop = route is null ? null : RouteAnalyzer.IspHop(route);

        var wifiReading = ReadWifi(adapter);

        var targets = new List<PingTarget>();
        if (gateway is not null)
            targets.Add(new PingTarget(NetworkTargetKind.Gateway, gateway.ToString()));
        if (ispHop?.Address is { } ispAddress && !string.Equals(ispAddress, end.Address, StringComparison.Ordinal))
            targets.Add(new PingTarget(NetworkTargetKind.IspHop, ispAddress));
        targets.Add(end);

        selfCpu.SampleCpuPercent();
        var startedAt = time.GetUtcNow();
        var interval = TimeSpan.FromMilliseconds(settings.Network.PingIntervalMs);
        var rounds = Math.Max(1, settings.GameNetwork.MeasureSeconds * 1000 / settings.Network.PingIntervalMs);
        var exited = false;
        bool KeepGoing()
        {
            if (identities.Count == 0)
                return true;
            var alive = identities.Any(IsAlive);
            exited |= !alive;
            return alive;
        }

        progress?.Report(new GameNetworkProgress(GameNetworkPhase.Measuring, 0, rounds));
        var series = await pingRounds.RunAsync(targets, rounds, interval, TimeSpan.FromMilliseconds(settings.Network.PingTimeoutMs), KeepGoing,
            new RoundProgress(done => progress?.Report(new GameNetworkProgress(GameNetworkPhase.Measuring, done, rounds))), ct).ConfigureAwait(false);

        var consumerReading = await ReadConsumersAsync(resolved, gameIds, time.GetElapsedTime(sessionStarted), ct).ConfigureAwait(false);
        var measurement = new GameNetworkMeasurement(GameNetworkMeasurement.CurrentSchemaVersion, IdFor(startedAt, game?.GameName), startedAt,
            interval, game?.GameName, string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim(), server, adapter, viaTunnel,
            series, route, wifiReading, consumerReading, activeActions.Current(),
            Reading<double>.Known(selfCpu.SampleCpuPercent(), SelfCpuSource), exited ? MeasurementEnd.GameExited : MeasurementEnd.Completed);

        progress?.Report(new GameNetworkProgress(GameNetworkPhase.Saving, 0, 0));
        await store.SaveAsync(measurement, ct).ConfigureAwait(false);
        var thresholds = config.Current.Thresholds;
        var analysis = GameNetworkAnalyzer.Analyze(measurement, thresholds.GameNetwork, thresholds.Network);
        return new GameNetworkRunResult(GameNetworkRunStatus.Saved, measurement, analysis);
    }

    private NetworkAdapterInfo? FindAdapter(IPAddress server)
    {
        try
        {
            var id = routes.AdapterIdFor(server);
            return id.IsAvailable ? adapters.Read().FirstOrDefault(a => string.Equals(a.Id, id.Value, StringComparison.Ordinal)) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSourceFailed(ex, "adapter");
            return null;
        }
    }

    private static IPAddress? FirstGatewayOf(NetworkAdapterInfo adapter, System.Net.Sockets.AddressFamily family)
    {
        foreach (var text in adapter.Gateways)
        {
            if (IPAddress.TryParse(text, out var address) && address.AddressFamily == family)
                return address;
        }
        return null;
    }

    private async Task<RouteTrace?> TraceAsync(IPAddress server, CancellationToken ct)
    {
        try
        {
            return await trace.RunAsync(server, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSourceFailed(ex, "traceroute");
            return null;
        }
    }

    private static PingTarget EndTarget(RouteTrace? route, string server)
    {
        if (route is null || route.ReachedTarget)
            return new PingTarget(NetworkTargetKind.Host, server);
        return RouteAnalyzer.LastResponding(route)?.Address is { } last
            ? new PingTarget(NetworkTargetKind.LastHop, last)
            : new PingTarget(NetworkTargetKind.Host, server);
    }

    private Reading<WifiStatus> ReadWifi(NetworkAdapterInfo? adapter)
    {
        if (adapter is not { Medium: NetworkMedium.Wireless })
            return Reading<WifiStatus>.Unsupported(WifiSource, "the route does not go through a Wi-Fi adapter");
        try
        {
            return wifi.Read(adapter.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSourceFailed(ex, "wifi");
            return Reading<WifiStatus>.Unknown(WifiSource, ex.GetType().Name);
        }
    }

    private bool IsAlive((int ProcessId, ProcessIdentity? Identity) game) =>
        game.Identity is { } identity
            ? processes.Identify(game.ProcessId) == identity
            : processes.List().Any(p => p.ProcessId == game.ProcessId);

    private static bool IsShortIpv4(string address) =>
        !address.Contains(':') && address.Split('.').Length != 4;

    private async Task<Reading<IReadOnlyList<ConsumerTraffic>>> ReadConsumersAsync(ResolvedServer resolved, IReadOnlySet<int> gameIds,
        TimeSpan window, CancellationToken ct)
    {
        if (resolved.Session is not { } session)
        {
            var status = resolved.TrafficStatus;
            return status.Status == DataStatus.RequiresAdmin
                ? Reading<IReadOnlyList<ConsumerTraffic>>.NeedsAdmin(status.Source ?? TrafficSource, status.Note)
                : Reading<IReadOnlyList<ConsumerTraffic>>.Unknown(status.Source ?? TrafficSource, status.Note);
        }

        try
        {
            if (session.DroppedEvents > 0)
                LogDroppedEvents(session.DroppedEvents);
            var list = await consumers.ResolveAsync(session.ReadTotals(), gameIds, window, ct).ConfigureAwait(false);
            return Reading<IReadOnlyList<ConsumerTraffic>>.Known(list, TrafficSource);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSourceFailed(ex, "consumers");
            return Reading<IReadOnlyList<ConsumerTraffic>>.Failure(TrafficSource, ex.GetType().Name);
        }
    }

    private static string IdFor(DateTimeOffset startedAt, string? gameName)
    {
        var safe = new StringBuilder();
        foreach (var c in gameName ?? "manual")
            safe.Append(char.IsControl(c) || InvalidFileNameChars.Contains(c) ? '_' : c);
        return startedAt.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + safe;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Traffic session dropped {Count} events; consumer figures may be understated")]
    private partial void LogDroppedEvents(long count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Game network measurement: {Part} read failed")]
    private partial void LogSourceFailed(Exception ex, string part);
}
