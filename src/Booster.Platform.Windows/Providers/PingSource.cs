using System.Net.NetworkInformation;
using Booster.Core.Abstractions;

namespace Booster.Platform.Windows.Providers;

/// <summary>Эхо-запрос ICMP через <see cref="Ping"/>: 32 байта, как у ping.exe.</summary>
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
            // Обычно внутри SocketException: имя не разрешилось или нет маршрута — повторять бессмысленно.
            throw new PingFailedException(ex.InnerException?.Message ?? ex.Message, ex);
        }
    }
}
