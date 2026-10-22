using Booster.Core.Abstractions;
using Booster.Core.Configuration;

namespace Booster.Core.GameNetwork;

/// <param name="Session">Живёт до конца замера, закрывает его владелец результата; null — ETW не стартовал.</param>
public sealed record ResolvedServer(MeasuredServer Server, ITrafficSession? Session, Reading<ITrafficSession> TrafficStatus);

public sealed class ServerResolver(ITrafficSource traffic, ITcpConnectionSource tcp, IBoosterConfiguration config, TimeProvider time)
{
    /// <returns>null — сервер не найден; сессия ETW в этом случае уже закрыта.</returns>
    public async Task<ResolvedServer?> ResolveAsync(string? manualServer, IReadOnlySet<int> gameProcessIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(gameProcessIds);

        var status = traffic.Start();
        var session = status.IsAvailable ? status.Value : null;
        var handedOver = false;
        try
        {
            if (manualServer is not null)
            {
                handedOver = true;
                return new ResolvedServer(new MeasuredServer(manualServer, ServerSource.Manual, []), session, status);
            }

            IReadOnlyList<ServerCandidate> candidates = [];
            var source = ServerSource.Etw;
            if (session is not null)
            {
                var started = time.GetTimestamp();
                await Task.Delay(TimeSpan.FromSeconds(config.Current.Settings.GameNetwork.ServerDetectSeconds), time, ct).ConfigureAwait(false);
                candidates = GameServerDetector.FromTraffic(session.ReadTotals(), gameProcessIds, time.GetElapsedTime(started),
                    config.Current.Thresholds.GameNetwork.MinGameEventsPerSecond);
            }

            if (candidates.Count == 0)
            {
                source = ServerSource.TcpTable;
                var connections = tcp.Read();
                if (connections.IsAvailable)
                    candidates = GameServerDetector.FromTcp(connections.Value!, gameProcessIds);
            }

            if (candidates.Count == 0)
                return null;

            handedOver = true;
            return new ResolvedServer(new MeasuredServer(candidates[0].Address, source, candidates), session, status);
        }
        finally
        {
            if (!handedOver)
                session?.Dispose();
        }
    }
}
