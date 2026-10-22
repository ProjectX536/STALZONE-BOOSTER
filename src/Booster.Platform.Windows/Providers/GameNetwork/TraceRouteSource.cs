using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using Booster.Core.Abstractions;
using Booster.Core.GameNetwork;

namespace Booster.Platform.Windows.Providers.GameNetwork;

public sealed class TraceRouteSource : ITraceRouteSource
{
    private static readonly byte[] Payload = new byte[32];

    public async Task<HopReply> ProbeAsync(IPAddress target, int ttl, TimeSpan timeout, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        using var ping = new Ping();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var reply = await ping.SendPingAsync(target, timeout, Payload, new PingOptions(ttl, dontFragment: true), ct).ConfigureAwait(false);
            return ToHop(reply.Status, reply.Address, reply.RoundtripTime, stopwatch.Elapsed);
        }
        catch (PingException ex)
        {
            throw new PingFailedException(ex.InnerException?.Message ?? ex.Message, ex);
        }
    }

    /// <summary>Null для статусов, которые не означают ответа удалённого узла (локальные и неопределённые сбои).</summary>
    public static HopStatus? Map(IPStatus status) => status switch
    {
        IPStatus.Success => HopStatus.Destination,
        IPStatus.TtlExpired or IPStatus.TimeExceeded => HopStatus.TtlExpired,
        IPStatus.TimedOut => HopStatus.Timeout,
        IPStatus.DestinationHostUnreachable or IPStatus.DestinationNetworkUnreachable or IPStatus.DestinationPortUnreachable
            or IPStatus.DestinationProhibited => HopStatus.Unreachable,
        _ => null,
    };

    public static HopReply ToHop(IPStatus status, IPAddress? address, long roundtripTimeMs, TimeSpan elapsed)
    {
        var hop = Map(status) ?? throw new PingFailedException($"ICMP status {status}");
        if (hop == HopStatus.Timeout)
            return new HopReply(hop, null, null);

        var replier = address is null || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ? null : address;
        // .NET отдаёт RoundtripTime 0 для TtlExpired; у Success и Unreachable 0 — настоящий субмиллисекундный ответ.
        var ms = hop == HopStatus.TtlExpired && roundtripTimeMs == 0 ? elapsed.TotalMilliseconds : roundtripTimeMs;
        return new HopReply(hop, replier, ms);
    }
}
