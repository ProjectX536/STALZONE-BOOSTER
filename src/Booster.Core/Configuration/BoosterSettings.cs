namespace Booster.Core.Configuration;

public sealed record BoosterSettings(
    int SchemaVersion,
    MonitoringSettings Monitoring,
    DetectorSettings Detectors,
    PresentMonSettings PresentMon,
    BenchmarkSettings Benchmark,
    LoggingSettings Logging,
    JournalSettings Journal,
    DiagnosticsSettings Diagnostics,
    NetworkSettings Network,
    GameNetworkSettings GameNetwork)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record MonitoringSettings(int SelfUsageIntervalMs, int GpuEngineSampleIntervalMs, int GameDetectionIntervalMs,
    int SensorIntervalMs, int GameExitConfirmationCount);

public sealed record DetectorSettings(int DefaultTimeoutMs, IReadOnlyDictionary<string, int> TimeoutsMs)
{
    public TimeSpan TimeoutFor(string sectionId) =>
        TimeSpan.FromMilliseconds(TimeoutsMs.TryGetValue(sectionId, out var ms) ? ms : DefaultTimeoutMs);
}

public sealed record PresentMonSettings(string? ExecutablePath, string SessionName);

public sealed record BenchmarkSettings(int RunCount, int RunSeconds, int PauseSeconds, int StartDelaySeconds, bool SoundCues,
    int RetainedSeriesCount, int SessionCaptureSeconds);

public sealed record LoggingSettings(int RetainedFileCount);

public sealed record JournalSettings(int RetainedSessionCount);

public sealed record DiagnosticsSettings(int StabilityDays, int MaxStabilityEvents, int TopProcessCount, int ActivityIntervalMs);

public sealed record NetworkSettings(int PingCount, int PingIntervalMs, int PingTimeoutMs, string? Host);

public sealed record GameNetworkSettings(int MeasureSeconds, int ServerDetectSeconds, int TraceMaxHops, int TraceProbesPerHop,
    int TraceStopAfterSilentHops, int LoadIdleSeconds, int LoadSeconds, int MaxTrackedFlows);
