using Booster.Core.Abstractions;
using Booster.Core.Reports;

namespace Booster.Core.SystemHealth;

/// <summary>Раздел системной диагностики, к которому относится рекомендация.</summary>
public enum AdviceModule
{
    Memory,
    Services,
    Stability,
    Network,
    Startup,
    Tweaks,

    /// <summary>Время DPC и прерываний по серии бенчмарка.</summary>
    Latency,
}

/// <summary>Рекомендации системной диагностики. Тексты — <c>AdviceId_*</c>, <c>AdviceText_*</c>, <c>AdviceAction_*</c>, <c>AdviceRisk_*</c>.</summary>
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

/// <summary>Рекомендация по снимку системы. Booster её не выполняет: уровень говорит, кто и как может это сделать.</summary>
/// <param name="Arguments">Подставляются в AdviceText_{Id}: числа — в инвариантной культуре, имена — как есть.</param>
public sealed record SystemAdvice(AdviceId Id, AdviceModule Module, FindingSeverity Severity, SafetyLevel Level,
    IReadOnlyList<string> Arguments);

/// <summary>Как задан размер файла подкачки.</summary>
public enum PagefileMode
{
    /// <summary>Размером всех файлов управляет система.</summary>
    SystemManaged,

    /// <summary>Файлы выбраны вручную, размер каждого выбирает система.</summary>
    SystemManagedSize,

    /// <summary>Размер задан вручную.</summary>
    Manual,

    /// <summary>Файла подкачки нет (или не будет после перезагрузки).</summary>
    Disabled,
}
