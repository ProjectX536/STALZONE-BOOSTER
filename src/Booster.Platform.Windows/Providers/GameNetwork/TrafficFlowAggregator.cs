using System.Net;
using System.Net.Sockets;
using Booster.Core.Abstractions;

namespace Booster.Platform.Windows.Providers.GameNetwork;

/// <summary>Суммы событий и байт по парам «процесс — удалённый адрес». Пишет поток ProcessTrace, читает поток замера.</summary>
public sealed class TrafficFlowAggregator(int maxFlows, IReadOnlySet<IPAddress> localAddresses)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<(int ProcessId, IPAddress Remote), Totals> _flows = [];

    // IPAddress.Equals сравнивает ScopeId, а у адресов из событий ETW его нет.
    private readonly HashSet<IPAddress> _local = [.. localAddresses.Select(WithoutScope)];

    private long _droppedEvents;

    public long DroppedEvents
    {
        get
        {
            lock (_lock)
                return _droppedEvents;
        }
    }

    public void Add(in KernelNetworkEvent e)
    {
        var remote = _local.Contains(e.Destination) ? e.Source : e.Destination;
        lock (_lock)
        {
            if (!_flows.TryGetValue((e.ProcessId, remote), out var totals))
            {
                if (_flows.Count >= maxFlows)
                {
                    _droppedEvents++;
                    return;
                }

                totals = new Totals();
                _flows.Add((e.ProcessId, remote), totals);
            }

            totals.Events++;
            totals.Bytes += e.Size;
        }
    }

    public IReadOnlyList<TrafficFlow> Snapshot()
    {
        lock (_lock)
            return [.. _flows.Select(f => new TrafficFlow(f.Key.ProcessId, f.Key.Remote, f.Value.Events, f.Value.Bytes))];
    }

    private static IPAddress WithoutScope(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 ? new IPAddress(address.GetAddressBytes()) : address;

    private sealed class Totals
    {
        public long Events;
        public long Bytes;
    }
}
