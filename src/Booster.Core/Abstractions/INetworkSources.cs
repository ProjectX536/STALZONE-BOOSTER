namespace Booster.Core.Abstractions;

public enum NetworkMedium
{
    Ethernet,
    Wireless,
    Tunnel,
    Other,
}

public sealed record NetworkAdapterInfo(string Id, string Name, string Description, NetworkMedium Medium, long? LinkSpeedBitsPerSecond,
    IReadOnlyList<string> Gateways);

public interface INetworkAdapterSource
{
    IReadOnlyList<NetworkAdapterInfo> Read();

    Reading<long> ReadTrafficBytes(string adapterId);
}

public interface IPingSource
{
    Task<double?> PingAsync(string host, TimeSpan timeout, CancellationToken ct);
}

public sealed class PingFailedException(string message, Exception? inner = null) : Exception(message, inner);
