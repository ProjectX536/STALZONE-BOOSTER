using System.Net;
using Booster.Core.Abstractions;

namespace Booster.Core.GameNetwork;

public static class GameServerDetector
{
    public const int MaxCandidates = 3;

    public static IReadOnlyList<ServerCandidate> FromTraffic(IReadOnlyList<TrafficFlow> flows, IReadOnlySet<int> gameProcessIds,
        TimeSpan window, double minEventsPerSecond)
    {
        ArgumentNullException.ThrowIfNull(flows);
        ArgumentNullException.ThrowIfNull(gameProcessIds);
        if (window <= TimeSpan.Zero)
            return [];

        return flows
            .Where(f => gameProcessIds.Contains(f.ProcessId) && AddressClassifier.IsPublic(f.Remote))
            .GroupBy(f => Normalize(f.Remote))
            .Select(g => new ServerCandidate(g.Key, g.Sum(f => f.Events) / window.TotalSeconds))
            .Where(c => c.EventsPerSecond >= minEventsPerSecond)
            .OrderByDescending(c => c.EventsPerSecond)
            .ThenBy(c => c.Address, StringComparer.Ordinal)
            .Take(MaxCandidates)
            .ToList();
    }

    public static IReadOnlyList<ServerCandidate> FromTcp(IReadOnlyList<TcpConnection> connections, IReadOnlySet<int> gameProcessIds)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(gameProcessIds);

        return connections
            .Where(c => c.Established && gameProcessIds.Contains(c.ProcessId) && AddressClassifier.IsPublic(c.Remote))
            .GroupBy(c => Normalize(c.Remote))
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Take(MaxCandidates)
            .Select(g => new ServerCandidate(g.Key, null))
            .ToList();
    }

    private static string Normalize(IPAddress address) =>
        (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
}
