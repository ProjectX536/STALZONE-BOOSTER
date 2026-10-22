using Booster.Core.Abstractions;
using Booster.Core.GameNetwork;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;
using static Booster.Core.Tests.TestSupport.SessionFactory;

namespace Booster.Core.Tests.GameNetwork;

public sealed class PingRoundRunnerTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PingTimeout = TimeSpan.FromMilliseconds(500);

    private static readonly PingTarget[] Targets =
    [
        new(NetworkTargetKind.Gateway, "192.168.1.1"),
        new(NetworkTargetKind.Host, "203.0.113.9"),
    ];

    private readonly FakePing _ping = new();
    private readonly FakeTimeProvider _time = new(Start);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<IReadOnlyList<TargetSeries>> Run(int rounds, Func<bool>? keepGoing = null, IProgress<int>? progress = null,
        CancellationToken? ct = null) =>
        new PingRoundRunner(_ping, _time).RunAsync(Targets, rounds, Interval, PingTimeout, keepGoing ?? (() => true), progress, ct ?? Ct);

    private static async Task WaitFor(Func<bool> condition)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition())
            await Task.Delay(5, cts.Token);
    }

    [Fact]
    public async Task Rounds_are_aligned_to_interval()
    {
        _ping.Replies["192.168.1.1"] = attempt => 1 + attempt;
        _ping.Replies["203.0.113.9"] = attempt => 20 + attempt;
        var progress = new RecordingProgress<int>();
        var run = Run(3, progress: progress);

        await WaitFor(() => progress.Items.Count == 1);
        Assert.Equal(2, _ping.Calls.Count);

        _time.Advance(TimeSpan.FromMilliseconds(999));
        await Task.Delay(50, Ct);
        Assert.Equal(2, _ping.Calls.Count);
        Assert.False(run.IsCompleted);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        await WaitFor(() => progress.Items.Count == 2);
        Assert.Equal(4, _ping.Calls.Count);
        Assert.False(run.IsCompleted);

        _time.Advance(Interval);
        var series = await run;

        Assert.Equal(6, _ping.Calls.Count);
        Assert.Equal(2, series.Count);
        Assert.Equal(NetworkTargetKind.Gateway, series[0].Kind);
        Assert.Equal([1.0, 2.0, 3.0], series[0].SamplesMs);
        Assert.Equal("203.0.113.9", series[1].Address);
        Assert.Equal([20.0, 21.0, 22.0], series[1].SamplesMs);
        Assert.Equal([1, 2, 3], progress.Items);
        Assert.Equal(PingTimeout, _ping.LastTimeout);
    }

    [Fact]
    public async Task Late_round_starts_immediately()
    {
        var gate = new TaskCompletionSource<double?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new SlowFirstPing(gate.Task);
        var run = new PingRoundRunner(slow, _time).RunAsync([Targets[0]], 2, Interval, PingTimeout, () => true, null, Ct);

        await WaitFor(() => slow.Calls == 1);
        _time.Advance(TimeSpan.FromSeconds(3));
        gate.SetResult(5);
        var series = await run;

        Assert.Equal([5.0, 7.0], series[0].SamplesMs);
    }

    [Fact]
    public async Task Failed_ping_is_null_sample()
    {
        _ping.Unresolvable.Add("203.0.113.9");
        _ping.Replies["192.168.1.1"] = _ => null;
        var progress = new RecordingProgress<int>();
        var run = Run(2, progress: progress);

        await WaitFor(() => progress.Items.Count == 1);
        _time.Advance(Interval);
        var series = await run;

        Assert.Equal([null, null], series[0].SamplesMs);
        Assert.Equal([null, null], series[1].SamplesMs);
    }

    [Fact]
    public async Task Keep_going_false_stops_early()
    {
        var allow = true;
        var progress = new RecordingProgress<int>();
        var run = Run(5, keepGoing: () => allow, progress: progress);

        await WaitFor(() => progress.Items.Count == 1);
        allow = false;
        _time.Advance(Interval);
        var series = await run;

        Assert.Single(series[0].SamplesMs);
        Assert.Single(series[1].SamplesMs);
        Assert.Equal(2, _ping.Calls.Count);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        var progress = new RecordingProgress<int>();
        var run = Run(3, progress: progress, ct: cts.Token);

        await WaitFor(() => progress.Items.Count == 1);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    private sealed class SlowFirstPing(Task<double?> first) : IPingSource
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<double?> PingAsync(string host, TimeSpan timeout, CancellationToken ct) =>
            Interlocked.Increment(ref _calls) == 1 ? first : Task.FromResult<double?>(7);
    }
}
