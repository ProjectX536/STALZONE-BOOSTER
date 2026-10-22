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
    GameNetwork,
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
    GameNetworkWifiScanSpikes,
    GameNetworkLocalSpikes,
    GameNetworkRemoteSpikes,
    GameNetworkRouteJump,
    GameNetworkFarServer,
    GameNetworkBufferbloat,
    GameNetworkVpn,
    GameNetworkBackgroundTraffic,
    GameNetworkDeliveryOptimization,
    GameNetworkWindowsUpdate,
    GameNetworkWifi24GHz,
    GameNetworkWifiWeakSignal,
    GameNetworkWireless,
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
