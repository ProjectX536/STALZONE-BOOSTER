using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Hags;
using Booster.Core.Inventory;
using Booster.Core.SystemHealth;

namespace Booster.Core.Reports;

public enum FindingSeverity
{
    Info,
    Warning,
}

public enum FindingKind
{
    DeviceError,
    DeviceDisabled,
    UnsignedDriver,
    LowDiskSpace,
    SectionFailed,
    SectionRequiresAdmin,
    FrameCaptureNeedsElevation,
    ConfigWarning,

    /// <summary>В последней серии бенчмарка найдены признаки ограничения производительности с уверенностью Medium или High.</summary>
    ThrottlingDetected,

    /// <summary>Рекомендация системной диагностики с важностью Warning; подробности — в разделе рекомендаций.</summary>
    SystemAdvice,
}

/// <summary>
/// Замечание по факту из снимка. <see cref="Subject"/> — к чему оно относится (устройство, том, секция, файл),
/// <see cref="Detail"/> — техническая подробность.
/// </summary>
public sealed record Finding(FindingKind Kind, FindingSeverity Severity, string Subject, string? Detail);

public sealed record ReportMetadata(int SchemaVersion, string AppVersion, DateTimeOffset GeneratedAt, PrivilegeState Privileges)
{
    /// <summary>Версия формата JSON-отчёта; растёт при несовместимом изменении структуры.</summary>
    public const int CurrentSchemaVersion = 4;
}

/// <param name="Advice">Рекомендации системной диагностики по снимку и тесту сети; Booster их не выполняет.</param>
/// <param name="Network">Последний тест сети; null — пользователь его не запускал.</param>
/// <param name="Tweaks">Настройки белого списка реестра и их основание на момент отчёта, в порядке каталога.</param>
/// <param name="Hags">Состояние A/B-теста HAGS; null — прочитать его не удалось. Booster HAGS не переключает.</param>
public sealed record SystemReport(
    ReportMetadata Metadata,
    SystemSnapshot Snapshot,
    IReadOnlyList<Finding> Findings,
    BenchmarkReport? Benchmark,
    IReadOnlyList<SystemAdvice> Advice,
    NetworkTestResult? Network,
    IReadOnlyList<TweakProposal> Tweaks,
    HagsExperimentView? Hags);
