using System.Net;
using Booster.Core.GameNetwork;

namespace Booster.Core.Abstractions;

public sealed record TrafficFlow(int ProcessId, IPAddress Remote, long Events, long Bytes);

public interface ITrafficSession : IDisposable
{
    IReadOnlyList<TrafficFlow> ReadTotals();

    long DroppedEvents { get; }
}

public interface ITrafficSource
{
    Reading<ITrafficSession> Start();

    void StopStaleSession();
}

public sealed record TcpConnection(int ProcessId, IPAddress Remote, int RemotePort, bool Established);

public interface ITcpConnectionSource
{
    Reading<IReadOnlyList<TcpConnection>> Read();
}

public sealed record HopReply(HopStatus Status, IPAddress? Address, double? RoundTripMs);

public interface ITraceRouteSource
{
    Task<HopReply> ProbeAsync(IPAddress target, int ttl, TimeSpan timeout, CancellationToken ct);
}

public interface IRouteSource
{
    /// <summary>Id совпадает с <see cref="NetworkAdapterInfo.Id"/>.</summary>
    Reading<string> AdapterIdFor(IPAddress destination);
}

public interface IWifiSource
{
    Reading<WifiStatus> Read(string adapterId);
}

public interface ISelfCpuSource
{
    /// <summary>Средний CPU Booster с прошлого вызова.</summary>
    double SampleCpuPercent();
}

public interface IActiveSessionActions
{
    /// <summary>ActionId со статусом Applied.</summary>
    IReadOnlyList<string> Current();
}
