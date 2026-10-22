using Booster.Core.Abstractions;
using Booster.Core.SystemHealth;

namespace Booster.Core.GameNetwork;

public sealed record PingTarget(NetworkTargetKind Kind, string Address);

public sealed class PingRoundRunner(IPingSource ping, TimeProvider time)
{
    public async Task<IReadOnlyList<TargetSeries>> RunAsync(IReadOnlyList<PingTarget> targets, int rounds, TimeSpan interval,
        TimeSpan timeout, Func<bool> keepGoing, IProgress<int>? progress, CancellationToken ct)
    {
        var samples = targets.Select(_ => new List<double?>()).ToArray();
        var started = time.GetTimestamp();

        for (var round = 0; round < rounds; round++)
        {
            if (round > 0)
            {
                var delay = interval * round - time.GetElapsedTime(started);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, time, ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            if (!keepGoing())
                break;

            var replies = await Task.WhenAll(targets.Select(t => PingAsync(t.Address, timeout, ct))).ConfigureAwait(false);
            for (var i = 0; i < replies.Length; i++)
                samples[i].Add(replies[i]);
            progress?.Report(round + 1);
        }

        return targets.Select((t, i) => new TargetSeries(t.Kind, t.Address, samples[i])).ToArray();
    }

    private async Task<double?> PingAsync(string address, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            return await ping.PingAsync(address, timeout, ct).ConfigureAwait(false);
        }
        catch (PingFailedException)
        {
            return null;
        }
    }
}
