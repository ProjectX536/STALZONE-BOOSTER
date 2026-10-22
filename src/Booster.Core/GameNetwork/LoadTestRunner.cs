using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.SystemHealth;

namespace Booster.Core.GameNetwork;

public enum LoadTestOutcome { Graded, NoLoad, NoReplies }

public sealed record LoadTestResult(DateTimeOffset StartedAt, NetworkTargetKind TargetKind, Reading<double> IdleMedianMs,
    Reading<double> LoadedMedianMs, Reading<double> ThroughputMbps, BufferbloatGrade? Grade, LoadTestOutcome Outcome);

public interface ILoadTestRunner
{
    Task<LoadTestResult> RunAsync(PingTarget target, string adapterId, Func<CancellationToken, Task> waitForLoad,
        IProgress<GameNetworkProgress>? progress, CancellationToken ct);
}

public sealed class LoadTestRunner(PingRoundRunner rounds, INetworkAdapterSource adapters, IBoosterConfiguration config, TimeProvider time)
    : ILoadTestRunner
{
    public const string ThroughputSource = "Adapter byte counters";

    private sealed class PhaseProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    public async Task<LoadTestResult> RunAsync(PingTarget target, string adapterId, Func<CancellationToken, Task> waitForLoad,
        IProgress<GameNetworkProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(adapterId);
        ArgumentNullException.ThrowIfNull(waitForLoad);

        var startedAt = time.GetUtcNow();
        var settings = config.Current.Settings;
        var interval = TimeSpan.FromMilliseconds(settings.Network.PingIntervalMs);
        var timeout = TimeSpan.FromMilliseconds(settings.Network.PingTimeoutMs);
        var idleRounds = Math.Max(1, settings.GameNetwork.LoadIdleSeconds * 1000 / settings.Network.PingIntervalMs);
        var loadRounds = Math.Max(1, settings.GameNetwork.LoadSeconds * 1000 / settings.Network.PingIntervalMs);
        var total = idleRounds + loadRounds;

        progress?.Report(new GameNetworkProgress(GameNetworkPhase.Measuring, 0, total));
        var idle = await PingPhaseAsync(target, idleRounds, 0, total, interval, timeout, progress, ct).ConfigureAwait(false);

        await waitForLoad(ct).ConfigureAwait(false);

        var before = adapters.ReadTrafficBytes(adapterId);
        var loadStarted = time.GetTimestamp();
        var loaded = await PingPhaseAsync(target, loadRounds, idleRounds, total, interval, timeout, progress, ct).ConfigureAwait(false);
        var after = adapters.ReadTrafficBytes(adapterId);
        var elapsed = time.GetElapsedTime(loadStarted);

        var idleMedian = NetworkAnalyzer.Summarize(target.Kind, target.Address, idle).MedianMs;
        var loadedMedian = NetworkAnalyzer.Summarize(target.Kind, target.Address, loaded).MedianMs;
        var throughput = Throughput(before, after, elapsed);

        var thresholds = config.Current.Thresholds.GameNetwork;
        if (!idleMedian.IsAvailable || !loadedMedian.IsAvailable)
            return new LoadTestResult(startedAt, target.Kind, idleMedian, loadedMedian, throughput, null, LoadTestOutcome.NoReplies);
        if (!throughput.IsAvailable || throughput.Value < thresholds.LoadMinThroughputMbps)
            return new LoadTestResult(startedAt, target.Kind, idleMedian, loadedMedian, throughput, null, LoadTestOutcome.NoLoad);

        var grade = BufferbloatGrader.Grade(loadedMedian.Value - idleMedian.Value, thresholds.BufferbloatGradesMs);
        return new LoadTestResult(startedAt, target.Kind, idleMedian, loadedMedian, throughput, grade, LoadTestOutcome.Graded);
    }

    private async Task<IReadOnlyList<double?>> PingPhaseAsync(PingTarget target, int phaseRounds, int doneBefore, int total,
        TimeSpan interval, TimeSpan timeout, IProgress<GameNetworkProgress>? progress, CancellationToken ct)
    {
        var series = await rounds.RunAsync([target], phaseRounds, interval, timeout, () => true,
            new PhaseProgress(done => progress?.Report(new GameNetworkProgress(GameNetworkPhase.Measuring, doneBefore + done, total))), ct)
            .ConfigureAwait(false);
        return series[0].SamplesMs;
    }

    private static Reading<double> Throughput(Reading<long> before, Reading<long> after, TimeSpan elapsed)
    {
        if (!before.IsAvailable || !after.IsAvailable)
            return Reading<double>.Unknown(ThroughputSource, "adapter byte counter is not readable");
        var delta = after.Value - before.Value;
        if (delta < 0 || elapsed <= TimeSpan.Zero)
            return Reading<double>.Unknown(ThroughputSource, "adapter byte counter was reset during the test");
        return Reading<double>.Known(delta * 8 / 1_000_000d / elapsed.TotalSeconds, ThroughputSource);
    }
}
