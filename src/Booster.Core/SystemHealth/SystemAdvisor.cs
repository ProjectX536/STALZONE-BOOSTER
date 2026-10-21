using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.Inventory;

namespace Booster.Core.SystemHealth;

public static class SystemAdvisor
{
    public static IReadOnlyList<SystemAdvice> Advise(SystemSnapshot snapshot, NetworkTestResult? network, Thresholds thresholds,
        SystemCatalog catalog, IReadOnlyList<string> gameExecutables, BenchmarkSeries? series)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(gameExecutables);

        var system = snapshot.System;
        var advice = new List<SystemAdvice>();
        advice.AddRange(MemoryAdvisor.Advise(system.Memory, system.Pagefile, snapshot.Hardware, thresholds.Memory,
            thresholds.LowFreeSpacePercent));
        if (system.Services is { IsAvailable: true, Value: { } services })
            advice.AddRange(ServiceClassifier.Advise(ServiceClassifier.Assess(services, catalog.Services)));
        if (system.Stability is { IsAvailable: true, Value: { } log })
            advice.AddRange(StabilityAnalyzer.Advise(log, gameExecutables));
        if (network is not null)
            advice.AddRange(NetworkAnalyzer.Advise(network, thresholds.Network));
        if (system.Startup is { IsAvailable: true, Value: { } startup })
            advice.AddRange(StartupAnalyzer.Advise(StartupAnalyzer.Assess(startup, thresholds.Startup, catalog.Services)));
        advice.AddRange(GamingAdvisor.Advise(system.Gaming, system.Overlays));
        advice.AddRange(TweakAdvisor.Advise(TweakAdvisor.Propose(snapshot, series, gameExecutables, catalog.RegistryTweaks)));
        advice.AddRange(LatencyAdvisor.Advise(series?.Telemetry, thresholds.Bottleneck));

        return advice.OrderBy(a => a.Module).ToArray();
    }
}
