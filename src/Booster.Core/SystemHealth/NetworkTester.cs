using Booster.Core.Abstractions;
using Booster.Core.Configuration;

namespace Booster.Core.SystemHealth;

/// <summary>
/// Тест сети по запросу пользователя: эхо-запросы к шлюзу и к узлу из настроек. Не работает в фоне и ничего не меняет.
/// </summary>
public sealed class NetworkTester(IPingSource ping, INetworkAdapterSource adapters, IBoosterConfiguration config, TimeProvider time)
{
    private const string UtilizationSource = "NetworkInterface statistics";

    private sealed class Target(NetworkTargetKind kind, string address)
    {
        public NetworkTargetKind Kind { get; } = kind;
        public string Address { get; } = address;
        public List<double?> Replies { get; } = [];
        public string? Failure { get; set; }
    }

    /// <summary>Progress — номер завершённого раунда (1..PingCount). Отмена — OperationCanceledException.</summary>
    public async Task<NetworkTestResult> RunAsync(IProgress<int>? progress, CancellationToken ct)
    {
        var settings = config.Current.Settings.Network;
        var startedAt = time.GetUtcNow();
        var started = time.GetTimestamp();

        var adapter = adapters.Read().FirstOrDefault(a => a.Gateways.Count > 0);
        var targets = new List<Target>();
        if (adapter is not null)
            targets.Add(new Target(NetworkTargetKind.Gateway, adapter.Gateways[0]));
        if (!string.IsNullOrWhiteSpace(settings.Host))
            targets.Add(new Target(NetworkTargetKind.Host, settings.Host.Trim()));

        var trafficBefore = adapter is null ? Reading<long>.Unknown() : adapters.ReadTrafficBytes(adapter.Id);
        var timeout = TimeSpan.FromMilliseconds(settings.PingTimeoutMs);
        var interval = TimeSpan.FromMilliseconds(settings.PingIntervalMs);
        for (var round = 1; round <= settings.PingCount && targets.Count > 0; round++)
        {
            await Task.WhenAll(targets.Where(t => t.Failure is null).Select(t => PingAsync(t, timeout, ct))).ConfigureAwait(false);
            progress?.Report(round);
            await Task.Delay(interval, time, ct).ConfigureAwait(false);
        }

        var duration = time.GetElapsedTime(started);
        var trafficAfter = adapter is null ? Reading<long>.Unknown() : adapters.ReadTrafficBytes(adapter.Id);
        return new NetworkTestResult(startedAt, duration, adapter, targets.Select(Result).ToArray(),
            Utilization(adapter, trafficBefore, trafficAfter, duration));
    }

    private async Task PingAsync(Target target, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            var reply = await ping.PingAsync(target.Address, timeout, ct).ConfigureAwait(false);
            target.Replies.Add(reply);
        }
        catch (PingFailedException ex)
        {
            // Узел недостижим в принципе (имя не разрешилось): дальше не опрашиваем, остальные цели — да.
            target.Failure = ex.Message;
        }
    }

    private static NetworkTargetResult Result(Target target)
    {
        if (target.Failure is null)
            return NetworkAnalyzer.Summarize(target.Kind, target.Address, target.Replies);

        var failure = Reading<double>.Failure(NetworkAnalyzer.Source, target.Failure);
        return new NetworkTargetResult(target.Kind, target.Address, target.Replies.Count, target.Replies.Count(r => r.HasValue),
            failure, failure, failure);
    }

    private static Reading<double> Utilization(NetworkAdapterInfo? adapter, Reading<long> before, Reading<long> after, TimeSpan duration)
    {
        if (adapter?.LinkSpeedBitsPerSecond is not { } speed || speed <= 0 || !before.IsAvailable || !after.IsAvailable
            || after.Value < before.Value || duration <= TimeSpan.Zero)
            return Reading<double>.Unknown(UtilizationSource);

        var bitsPerSecond = (after.Value - before.Value) * 8.0 / duration.TotalSeconds;
        return Reading<double>.Known(bitsPerSecond / speed * 100, UtilizationSource);
    }
}
