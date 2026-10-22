using Booster.Core.Abstractions;
using Booster.Core.GameNetwork;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;
using static Booster.Core.Tests.TestSupport.SessionFactory;

namespace Booster.Core.Tests.GameNetwork;

public sealed class LoadTestRunnerTests
{
    private const string Address = "203.0.113.9";
    private static readonly int IdleRounds =
        FakeConfiguration.DefaultGameNetwork.LoadIdleSeconds * 1000 / FakeConfiguration.DefaultNetwork.PingIntervalMs;
    private static readonly int LoadRounds =
        FakeConfiguration.DefaultGameNetwork.LoadSeconds * 1000 / FakeConfiguration.DefaultNetwork.PingIntervalMs;
    private static readonly int TotalRounds = IdleRounds + LoadRounds;

    private static readonly PingTarget Target = new(NetworkTargetKind.Host, Address);

    private readonly FakePing _ping = new();
    private readonly FakeNetworkAdapters _adapters = new();
    private readonly FakeTimeProvider _time = new(Start);
    private readonly RecordingProgress<GameNetworkProgress> _progress = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private LoadTestRunner Runner() =>
        new(new PingRoundRunner(_ping, _time), _adapters, new FakeConfiguration(), _time);

    private Task<LoadTestResult> Run(Func<CancellationToken, Task>? waitForLoad = null, CancellationToken? ct = null) =>
        Runner().RunAsync(Target, "eth0", waitForLoad ?? (_ => Task.CompletedTask), _progress, ct ?? Ct);

    private static async Task WaitFor(Func<bool> condition)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition())
            await Task.Delay(5, cts.Token);
    }

    private int Done => _progress.Items.Count == 0 ? 0 : _progress.Items[^1].Done;

    private async Task Drive(int fromRound, int toRound)
    {
        for (var round = fromRound; round < toRound; round++)
        {
            await WaitFor(() => Done >= round);
            await Task.Delay(20, Ct);
            _time.Advance(TimeSpan.FromSeconds(1));
        }
    }

    private void Traffic(long before, long after)
    {
        _adapters.Traffic.Enqueue(Reading<long>.Known(before, "fake"));
        _adapters.Traffic.Enqueue(Reading<long>.Known(after, "fake"));
    }

    private static long BytesFor(double mbps) => (long)(mbps * 1_000_000 / 8 * (LoadRounds - 1));

    private async Task<LoadTestResult> RunAll(Func<CancellationToken, Task>? waitForLoad = null)
    {
        var run = Run(waitForLoad);
        await Drive(1, TotalRounds);
        return await run;
    }

    [Fact]
    public async Task Graded_from_median_increase()
    {
        _ping.Replies[Address] = attempt => attempt < IdleRounds ? 20 : 95;
        Traffic(1_000, 1_000 + BytesFor(50));

        var result = await RunAll();

        Assert.Equal(LoadTestOutcome.Graded, result.Outcome);
        Assert.Equal(BufferbloatGrade.C, result.Grade);
        Assert.Equal(20, result.IdleMedianMs.Value);
        Assert.Equal(95, result.LoadedMedianMs.Value);
        Assert.Equal(50, result.ThroughputMbps.Value, 6);
        Assert.Equal(NetworkTargetKind.Host, result.TargetKind);
        Assert.Equal(Start, result.StartedAt);
        Assert.Equal(TotalRounds, _ping.Calls.Count);
    }

    [Fact]
    public async Task Low_throughput_is_no_load()
    {
        _ping.Replies[Address] = attempt => attempt < IdleRounds ? 20 : 95;
        Traffic(0, BytesFor(2));

        var result = await RunAll();

        Assert.Equal(LoadTestOutcome.NoLoad, result.Outcome);
        Assert.Null(result.Grade);
        Assert.True(result.ThroughputMbps.IsAvailable);
    }

    [Fact]
    public async Task Unknown_traffic_counter_is_no_load()
    {
        _adapters.Traffic.Enqueue(Reading<long>.Unknown("fake"));
        _adapters.Traffic.Enqueue(Reading<long>.Known(BytesFor(50), "fake"));

        var result = await RunAll();

        Assert.Equal(LoadTestOutcome.NoLoad, result.Outcome);
        Assert.Null(result.Grade);
        Assert.False(result.ThroughputMbps.IsAvailable);
    }

    [Fact]
    public async Task Counter_reset_is_no_load()
    {
        Traffic(BytesFor(50), 100);

        var result = await RunAll();

        Assert.Equal(LoadTestOutcome.NoLoad, result.Outcome);
        Assert.False(result.ThroughputMbps.IsAvailable);
    }

    [Fact]
    public async Task Silent_target_is_no_replies()
    {
        _ping.Replies[Address] = _ => null;
        Traffic(0, BytesFor(50));

        var result = await RunAll();

        Assert.Equal(LoadTestOutcome.NoReplies, result.Outcome);
        Assert.Null(result.Grade);
        Assert.False(result.IdleMedianMs.IsAvailable);
    }

    [Fact]
    public async Task Loaded_phase_waits_for_user()
    {
        _ping.Replies[Address] = attempt => attempt < IdleRounds ? 20 : 95;
        Traffic(0, BytesFor(50));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = Run(_ => gate.Task);

        await Drive(1, IdleRounds);
        await WaitFor(() => Done == IdleRounds);
        _time.Advance(TimeSpan.FromSeconds(60));
        await Task.Delay(50, Ct);

        Assert.Equal(IdleRounds, _ping.Calls.Count);
        Assert.False(run.IsCompleted);

        gate.SetResult();
        await Drive(IdleRounds + 1, TotalRounds);
        var result = await run;

        Assert.Equal(LoadTestOutcome.Graded, result.Outcome);
        Assert.Equal(TotalRounds, _ping.Calls.Count);
    }

    [Fact]
    public async Task Progress_spans_both_phases()
    {
        Traffic(0, BytesFor(50));

        await RunAll();

        var items = _progress.Items;
        Assert.All(items, p => Assert.Equal(GameNetworkPhase.Measuring, p.Phase));
        Assert.All(items, p => Assert.Equal(TotalRounds, p.Total));
        Assert.Equal(Enumerable.Range(0, TotalRounds + 1), items.Select(p => p.Done));
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = Run(async token =>
        {
            await gate.Task.WaitAsync(token);
        }, cts.Token);

        await Drive(1, IdleRounds);
        await WaitFor(() => Done == IdleRounds);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }
}
