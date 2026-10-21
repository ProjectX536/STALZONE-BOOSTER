using Booster.Core.Abstractions;
using Booster.Core.Reports;

namespace Booster.Core.SystemHealth;

public enum AdviceModule
{
    Memory,
    Services,
    Stability,
    Network,
    Startup,
    Tweaks,

    Latency,
}

public enum AdviceId
{
    PagefileDisabled,
    PagefileManual,
    CommitNearLimit,
    CommitPeakNearLimit,
    PagefileVolumeLowSpace,
    PagefileOnHdd,
    MemoryBelowRatedSpeed,
    SingleMemoryModule,
    MixedMemoryModules,
    ThirdPartyServicesAutostart,
    UnexpectedShutdowns,
    BugChecks,
    DisplayDriverResets,
    HardwareErrors,
    DiskErrors,
    GameCrashes,
    NetworkHighLatency,
    NetworkJitter,
    NetworkLoss,
    NetworkWireless,
    HeavyStartupItems,
    HeavyMachineStartupItems,
    OverlaysRunning,
    BackgroundRecordingOn,
    GameModeOff,
    GpuOnIntegrated,
    LegacyWindowedPresent,
    DpcTimeHigh,
    InterruptTimeHigh,
}

public sealed record SystemAdvice(AdviceId Id, AdviceModule Module, FindingSeverity Severity, SafetyLevel Level,
    IReadOnlyList<string> Arguments);

public enum PagefileMode
{
    SystemManaged,

    SystemManagedSize,

    Manual,

    Disabled,
}
