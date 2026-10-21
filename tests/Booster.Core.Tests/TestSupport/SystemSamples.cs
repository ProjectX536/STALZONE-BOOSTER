using Booster.Core.Abstractions;
using Booster.Core.Inventory;

namespace Booster.Core.Tests.TestSupport;

internal static class SystemSamples
{
    public const string Source = "test";
    public const string Gateway = "192.168.1.1";
    private const long GiB = 1L << 30;
    private const long MiB = 1L << 20;

    public static SystemDiagnostics Healthy() => new(
        Known(Memory()),
        Known(Pagefile()),
        Known<IReadOnlyList<ProcessMemoryUsage>>(Processes),
        Known<IReadOnlyList<ServiceInfo>>([Service("RpcSs"), Service("Dnscache"), Service("Spooler")]),
        Known(new StabilityLog(SnapshotFactory.TakenAt.AddDays(-14), [], Truncated: false)),
        Known<IReadOnlyList<NetworkAdapterInfo>>([FakeNetworkAdapters.Adapter("eth0", NetworkMedium.Ethernet, 1_000_000_000, Gateway)]),
        Known<IReadOnlyList<StartupItem>>([]),
        Known(new GamingSettings(Reading<bool>.Known(true, Source), Reading<bool>.Known(false, Source))),
        Known<IReadOnlyList<RunningOverlay>>([]),
        Reading<bool>.Known(true, Source));

    public static MemoryStatus Memory(long commitTotalBytes = 16 * GiB, long commitLimitBytes = 40 * GiB) => new(
        PhysicalTotalBytes: 32 * GiB, PhysicalAvailableBytes: 16 * GiB, CommitTotalBytes: commitTotalBytes, CommitLimitBytes: commitLimitBytes,
        CommitPeakBytes: commitTotalBytes, SystemCacheBytes: 4 * GiB, KernelPagedBytes: 512 * MiB, KernelNonPagedBytes: 256 * MiB,
        ProcessCount: 250, ThreadCount: 3500, HandleCount: 120_000);

    public static PagefileConfiguration Pagefile() =>
        new(Known(true), [], [new PagefileUsage(@"C:\pagefile.sys", 4096, 512, 1024)]);

    public static readonly IReadOnlyList<ProcessMemoryUsage> Processes =
    [
        new("chrome.exe", 4242, 900 * MiB, 700 * MiB),
        new("Discord.exe", 5150, 400 * MiB, 300 * MiB),
        new("explorer.exe", 1234, 200 * MiB, 120 * MiB),
    ];

    public static ServiceInfo Service(string name, string publisher = "Microsoft Corporation") => new(
        name, $"{name} display", ServiceRunState.Running, ServiceStartMode.Automatic, DelayedAutoStart: false,
        $@"C:\Windows\System32\{name}.dll", ProcessId: 1000, Known(publisher), []);

    private static Reading<T> Known<T>(T value) => Reading<T>.Known(value, Source);
}
