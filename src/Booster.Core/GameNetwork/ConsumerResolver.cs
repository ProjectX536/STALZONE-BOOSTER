using Booster.Core.Abstractions;
using Booster.Core.Configuration;

namespace Booster.Core.GameNetwork;

public sealed class ConsumerResolver(IProcessProvider processes, IServiceInventorySource services, IBoosterConfiguration config)
{
    public async Task<IReadOnlyList<ConsumerTraffic>> ResolveAsync(IReadOnlyList<TrafficFlow> flows, IReadOnlySet<int> gameProcessIds,
        TimeSpan window, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        var bytesByPid = flows
            .Where(f => !gameProcessIds.Contains(f.ProcessId))
            .GroupBy(f => f.ProcessId)
            .Select(g => (ProcessId: g.Key, Bytes: g.Sum(f => f.Bytes)))
            .ToArray();
        if (bytesByPid.Length == 0)
            return [];

        var names = new Dictionary<int, string>();
        foreach (var process in processes.List())
            names[process.ProcessId] = process.Name;
        var servicesByPid = (await services.ReadAsync(ct).ConfigureAwait(false))
            .Where(s => s.ProcessId is not null)
            .ToLookup(s => s.ProcessId!.Value, s => s.Name);

        var catalog = config.Current.Catalog.NetworkConsumers;
        var seconds = window.TotalSeconds;
        return bytesByPid
            .Select(p =>
            {
                var pidServices = servicesByPid[p.ProcessId].Order(StringComparer.Ordinal).ToArray();
                var name = names.GetValueOrDefault(p.ProcessId);
                return new ConsumerTraffic(p.ProcessId, name ?? $"PID {p.ProcessId}", pidServices, KindOf(catalog, pidServices, name),
                    p.Bytes / 1024.0 / seconds);
            })
            .OrderByDescending(c => c.KBytesPerSecond)
            .ThenBy(c => c.ProcessId)
            .Take(config.Current.Settings.Diagnostics.TopProcessCount)
            .ToArray();
    }

    private static NetworkConsumerKind? KindOf(IReadOnlyList<NetworkConsumerDefinition> catalog, string[] services, string? processName)
    {
        foreach (var definition in catalog)
        {
            if (definition.Service is not null && services.Contains(definition.Service, StringComparer.OrdinalIgnoreCase))
                return definition.Kind;
        }

        if (processName is null)
            return null;
        var process = WithoutExe(processName);
        foreach (var definition in catalog)
        {
            if (definition.Process is not null && string.Equals(WithoutExe(definition.Process), process, StringComparison.OrdinalIgnoreCase))
                return definition.Kind;
        }

        return null;
    }

    private static string WithoutExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
