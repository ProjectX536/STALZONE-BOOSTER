using System.Net;
using Booster.Core.Abstractions;
using Booster.Core.GameNetwork;

namespace Booster.Core.Tests.TestSupport;

public sealed class FakeTrafficSession : ITrafficSession
{
    public IReadOnlyList<TrafficFlow> Flows { get; set; } = [];

    public long DroppedEvents { get; set; }

    public bool Disposed { get; private set; }

    public IReadOnlyList<TrafficFlow> ReadTotals() => Flows;

    public void Dispose() => Disposed = true;
}

public sealed class FakeTrafficSource : ITrafficSource
{
    public FakeTrafficSource() => Result = Reading<ITrafficSession>.Known(Session, "fake");

    public FakeTrafficSession Session { get; } = new();

    public Reading<ITrafficSession> Result { get; set; }

    public int Starts { get; private set; }

    public int StaleStops { get; private set; }

    public Reading<ITrafficSession> Start()
    {
        Starts++;
        return Result;
    }

    public void StopStaleSession() => StaleStops++;
}

public sealed class FakeTcpConnections : ITcpConnectionSource
{
    public Reading<IReadOnlyList<TcpConnection>> Result { get; set; } = Reading<IReadOnlyList<TcpConnection>>.Known([], "fake");

    public Reading<IReadOnlyList<TcpConnection>> Read() => Result;
}

public sealed class FakeTraceRoute : ITraceRouteSource
{
    public Dictionary<int, HopReply> Hops { get; } = [];

    public Task<HopReply> ProbeAsync(IPAddress target, int ttl, TimeSpan timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Hops.TryGetValue(ttl, out var reply) ? reply : new HopReply(HopStatus.Timeout, null, null));
    }
}

public sealed class FakeRouteSource : IRouteSource
{
    public Reading<string> Result { get; set; } = Reading<string>.Unknown("fake");

    public Reading<string> AdapterIdFor(IPAddress destination) => Result;
}

public sealed class FakeWifiSource : IWifiSource
{
    public Reading<WifiStatus> Result { get; set; } = Reading<WifiStatus>.Unknown("fake");

    public Reading<WifiStatus> Read(string adapterId) => Result;
}

public sealed class FakeSelfCpu : ISelfCpuSource
{
    public Queue<double> Values { get; } = new();

    public double SampleCpuPercent() => Values.Count > 0 ? Values.Dequeue() : 0;
}

public sealed class FakeActiveSessionActions : IActiveSessionActions
{
    public List<string> Actions { get; } = [];

    public IReadOnlyList<string> Current() => Actions.ToArray();
}

public sealed class InMemoryGameNetworkStore : IGameNetworkStore
{
    private readonly Lock _lock = new();

    public List<GameNetworkMeasurement> Saved { get; } = [];

    public Task SaveAsync(GameNetworkMeasurement measurement, CancellationToken ct)
    {
        lock (_lock)
            Saved.Add(measurement);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GameNetworkMeasurement>> LoadAllAsync(CancellationToken ct)
    {
        lock (_lock)
            return Task.FromResult<IReadOnlyList<GameNetworkMeasurement>>(Saved.OrderByDescending(m => m.StartedAt).ToArray());
    }

    public Task DeleteAsync(string id, CancellationToken ct)
    {
        lock (_lock)
            Saved.RemoveAll(m => m.Id == id);
        return Task.CompletedTask;
    }
}

public sealed class FakeGameNetworkMeasurer(IGameNetworkStore? store = null) : IGameNetworkMeasurer
{
    private readonly Lock _lock = new();

    public List<GameNetworkRequest> Requests { get; } = [];

    public Func<CancellationToken, Task<GameNetworkRunResult>> Run { get; set; } =
        _ => Task.FromResult(new GameNetworkRunResult(GameNetworkRunStatus.NoGame, null, null));

    public void Saves(GameNetworkMeasurement measurement) => Run = async ct =>
    {
        if (store is not null)
            await store.SaveAsync(measurement, ct);
        return new GameNetworkRunResult(GameNetworkRunStatus.Saved, measurement, GameNetworkSamples.Analysis(measurement));
    };

    public Task<GameNetworkRunResult> MeasureAsync(GameNetworkRequest request, IProgress<GameNetworkProgress>? progress, CancellationToken ct)
    {
        lock (_lock)
            Requests.Add(request);
        return Run(ct);
    }
}

public sealed class FakeLoadTestRunner : ILoadTestRunner
{
    public TaskCompletionSource IdleDone { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource LoadDone { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public LoadTestResult Result { get; set; } = GameNetworkSamples.Load();

    public PingTarget? Target { get; private set; }

    public string? AdapterId { get; private set; }

    public async Task<LoadTestResult> RunAsync(PingTarget target, string adapterId, Func<CancellationToken, Task> waitForLoad,
        IProgress<GameNetworkProgress>? progress, CancellationToken ct)
    {
        Target = target;
        AdapterId = adapterId;
        await IdleDone.Task.WaitAsync(ct);
        await waitForLoad(ct);
        await LoadDone.Task.WaitAsync(ct);
        return Result;
    }
}
