using System.Net;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;

namespace Booster.Core.GameNetwork;

public sealed class TraceRouteRunner(ITraceRouteSource source, IBoosterConfiguration config)
{
    public async Task<RouteTrace> RunAsync(IPAddress target, CancellationToken ct)
    {
        var settings = config.Current.Settings;
        var game = settings.GameNetwork;
        var timeout = TimeSpan.FromMilliseconds(settings.Network.PingTimeoutMs);
        var hops = new List<RouteHop>();
        var silentRun = 0;
        var reached = false;

        for (var ttl = 1; ttl <= game.TraceMaxHops; ttl++)
        {
            var probes = new List<double?>(game.TraceProbesPerHop);
            IPAddress? address = null;
            var destination = false;
            for (var probe = 0; probe < game.TraceProbesPerHop; probe++)
            {
                var reply = await ProbeAsync(target, ttl, timeout, ct).ConfigureAwait(false);
                probes.Add(reply.RoundTripMs);
                address ??= reply.Address;
                destination |= reply.Status == HopStatus.Destination;
            }

            hops.Add(new RouteHop(ttl, address?.ToString(), address is null ? null : AddressClassifier.Classify(address), probes));
            if (destination)
            {
                reached = true;
                break;
            }

            silentRun = address is null ? silentRun + 1 : 0;
            if (silentRun >= game.TraceStopAfterSilentHops)
            {
                hops.RemoveRange(hops.Count - silentRun, silentRun);
                break;
            }
        }

        return new RouteTrace(target.ToString(), hops, reached);
    }

    private async Task<HopReply> ProbeAsync(IPAddress target, int ttl, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            return await source.ProbeAsync(target, ttl, timeout, ct).ConfigureAwait(false);
        }
        catch (PingFailedException)
        {
            return new HopReply(HopStatus.Timeout, null, null);
        }
    }
}
