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

    ThrottlingDetected,

    SystemAdvice,
}

public sealed record Finding(FindingKind Kind, FindingSeverity Severity, string Subject, string? Detail);

public sealed record ReportMetadata(int SchemaVersion, string AppVersion, DateTimeOffset GeneratedAt, PrivilegeState Privileges)
{
    public const int CurrentSchemaVersion = 5;
}

public sealed record SystemReport(
    ReportMetadata Metadata,
    SystemSnapshot Snapshot,
    IReadOnlyList<Finding> Findings,
    BenchmarkReport? Benchmark,
    IReadOnlyList<SystemAdvice> Advice,
    NetworkTestResult? Network,
    IReadOnlyList<TweakProposal> Tweaks,
    HagsExperimentView? Hags,
    GameNetworkReportView? GameNetwork);
