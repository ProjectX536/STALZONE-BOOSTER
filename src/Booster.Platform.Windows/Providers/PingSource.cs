using System.Net.NetworkInformation;
using Booster.Core.Abstractions;

namespace Booster.Platform.Windows.Providers;

public sealed class PingSource : IPingSource
{
    private static readonly byte[] Payload = new byte[32];

    public async Task<double?> PingAsync(string host, TimeSpan timeout, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(host, timeout, Payload, options: null, ct).ConfigureAwait(false);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (PingException ex)
        {
            throw new PingFailedException(ex.InnerException?.Message ?? ex.Message, ex);
        }
    }
}
