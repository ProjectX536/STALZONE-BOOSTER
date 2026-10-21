using Booster.Core.Abstractions;

namespace Booster.Core.Inventory;

public sealed record SystemDiagnostics(
    Reading<MemoryStatus> Memory,
    Reading<PagefileConfiguration> Pagefile,
    Reading<IReadOnlyList<ProcessMemoryUsage>> TopMemoryProcesses,
    Reading<IReadOnlyList<ServiceInfo>> Services,
    Reading<StabilityLog> Stability,
    Reading<IReadOnlyList<NetworkAdapterInfo>> NetworkAdapters,
    Reading<IReadOnlyList<StartupItem>> Startup,
    Reading<GamingSettings> Gaming,
    Reading<IReadOnlyList<RunningOverlay>> Overlays,
    Reading<bool> Hags);

public sealed record StartupItem(StartupEntry Entry, long? PrivateBytes);

public sealed record RunningOverlay(string Process, string Name, IReadOnlyList<int> ProcessIds);
