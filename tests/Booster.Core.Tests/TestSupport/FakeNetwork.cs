using Booster.Core.Abstractions;

namespace Booster.Core.Tests.TestSupport;

public sealed class FakePing : IPingSource
{
    private readonly object _gate = new();
    private readonly List<string> _calls = [];

    public Dictionary<string, Func<int, double?>> Replies { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Unresolvable { get; } = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate)
                return _calls.ToArray();
        }
    }

    public TimeSpan? LastTimeout { get; private set; }

    public Task<double?> PingAsync(string host, TimeSpan timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        int attempt;
        lock (_gate)
        {
            attempt = _calls.Count(c => c == host);
            _calls.Add(host);
            LastTimeout = timeout;
        }

        if (Unresolvable.Contains(host))
            throw new PingFailedException($"No such host is known: {host}");
        return Task.FromResult(Replies.TryGetValue(host, out var reply) ? reply(attempt) : 10.0);
    }
}

public sealed class FakeNetworkAdapters : INetworkAdapterSource
{
    public List<NetworkAdapterInfo> Adapters { get; } = [];

    public Queue<Reading<long>> Traffic { get; } = new();

    public IReadOnlyList<NetworkAdapterInfo> Read() => Adapters;

    public Reading<long> ReadTrafficBytes(string adapterId) =>
        Traffic.Count > 0 ? Traffic.Dequeue() : Reading<long>.Unknown("fake");

    public static NetworkAdapterInfo Adapter(string id = "eth0", NetworkMedium medium = NetworkMedium.Ethernet, long? speed = 100_000_000,
        params string[] gateways) =>
        new(id, $"{id} name", $"{id} description", medium, speed, gateways);
}

public sealed class RecordingProgress<T> : IProgress<T>
{
    private readonly object _gate = new();
    private readonly List<T> _items = [];

    public IReadOnlyList<T> Items
    {
        get
        {
            lock (_gate)
                return _items.ToArray();
        }
    }

    public void Report(T value)
    {
        lock (_gate)
            _items.Add(value);
    }
}
