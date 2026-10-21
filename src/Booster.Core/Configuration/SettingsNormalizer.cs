namespace Booster.Core.Configuration;

public static class SettingsNormalizer
{
    private const int MinimumMonitoringIntervalMs = 1000;

    private const int MinimumTimeoutMs = 1000;
    private const int MinimumRetainedFiles = 1;
    private const int MinimumRetainedSessions = 1;
    private const int MinimumRetainedSeries = 1;

    public const int MinimumRunCount = 1;
    public const int MaximumRunCount = 10;

    public const int MinimumRunSeconds = 10;
    public const int MaximumRunSeconds = 600;

    public const int MinimumPauseSeconds = 0;
    public const int MaximumPauseSeconds = 300;

    public const int MinimumStartDelaySeconds = 3;
    public const int MaximumStartDelaySeconds = 300;

    private const int MinimumSessionCaptureSeconds = 30;
    private const int MaximumSessionCaptureSeconds = 1800;

    private const int MinimumGameExitConfirmations = 1;
    private const int MaximumGameExitConfirmations = 10;

    private const int MinimumStabilityDays = 1;
    private const int MaximumStabilityDays = 90;

    private const int MinimumStabilityEvents = 10;
    private const int MaximumStabilityEvents = 5000;

    private const int MinimumTopProcessCount = 3;
    private const int MaximumTopProcessCount = 50;

    private const int MinimumPingCount = 5;
    private const int MaximumPingCount = 600;

    private const int MinimumPingIntervalMs = 200;
    private const int MaximumPingIntervalMs = 10_000;

    private const int MinimumPingTimeoutMs = 100;
    private const int MaximumPingTimeoutMs = 5000;

    public static ConfigLoadResult<BoosterSettings> Normalize(BoosterSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var warnings = new List<ConfigWarning>();

        int AtLeast(int value, int minimum, string key)
        {
            if (value >= minimum)
                return value;
            warnings.Add(new(ConfigWarningKind.ValueClamped, ConfigFiles.Settings, $"{key}: {value} -> {minimum}"));
            return minimum;
        }

        int Between(int value, int minimum, int maximum, string key)
        {
            if (value <= maximum)
                return AtLeast(value, minimum, key);
            warnings.Add(new(ConfigWarningKind.ValueClamped, ConfigFiles.Settings, $"{key}: {value} -> {maximum}"));
            return maximum;
        }

        var monitoring = new MonitoringSettings(
            AtLeast(s.Monitoring.SelfUsageIntervalMs, MinimumMonitoringIntervalMs, "monitoring.selfUsageIntervalMs"),
            AtLeast(s.Monitoring.GpuEngineSampleIntervalMs, MinimumMonitoringIntervalMs, "monitoring.gpuEngineSampleIntervalMs"),
            AtLeast(s.Monitoring.GameDetectionIntervalMs, MinimumMonitoringIntervalMs, "monitoring.gameDetectionIntervalMs"),
            AtLeast(s.Monitoring.SensorIntervalMs, MinimumMonitoringIntervalMs, "monitoring.sensorIntervalMs"),
            Between(s.Monitoring.GameExitConfirmationCount, MinimumGameExitConfirmations, MaximumGameExitConfirmations,
                "monitoring.gameExitConfirmationCount"));

        var timeouts = s.Detectors.TimeoutsMs
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ToDictionary(p => p.Key, p => AtLeast(p.Value, MinimumTimeoutMs, $"detectors.timeoutsMs.{p.Key}"));
        var detectors = new DetectorSettings(
            AtLeast(s.Detectors.DefaultTimeoutMs, MinimumTimeoutMs, "detectors.defaultTimeoutMs"), timeouts);

        var b = s.Benchmark;
        var benchmark = b with
        {
            RunCount = Between(b.RunCount, MinimumRunCount, MaximumRunCount, "benchmark.runCount"),
            RunSeconds = Between(b.RunSeconds, MinimumRunSeconds, MaximumRunSeconds, "benchmark.runSeconds"),
            PauseSeconds = Between(b.PauseSeconds, MinimumPauseSeconds, MaximumPauseSeconds, "benchmark.pauseSeconds"),
            StartDelaySeconds = Between(b.StartDelaySeconds, MinimumStartDelaySeconds, MaximumStartDelaySeconds, "benchmark.startDelaySeconds"),
            RetainedSeriesCount = AtLeast(b.RetainedSeriesCount, MinimumRetainedSeries, "benchmark.retainedSeriesCount"),
            SessionCaptureSeconds = Between(b.SessionCaptureSeconds, MinimumSessionCaptureSeconds, MaximumSessionCaptureSeconds,
                "benchmark.sessionCaptureSeconds"),
        };
        var logging = new LoggingSettings(
            AtLeast(s.Logging.RetainedFileCount, MinimumRetainedFiles, "logging.retainedFileCount"));
        var journal = new JournalSettings(
            AtLeast(s.Journal.RetainedSessionCount, MinimumRetainedSessions, "journal.retainedSessionCount"));

        var d = s.Diagnostics;
        var diagnostics = new DiagnosticsSettings(
            Between(d.StabilityDays, MinimumStabilityDays, MaximumStabilityDays, "diagnostics.stabilityDays"),
            Between(d.MaxStabilityEvents, MinimumStabilityEvents, MaximumStabilityEvents, "diagnostics.maxStabilityEvents"),
            Between(d.TopProcessCount, MinimumTopProcessCount, MaximumTopProcessCount, "diagnostics.topProcessCount"),
            AtLeast(d.ActivityIntervalMs, MinimumMonitoringIntervalMs, "diagnostics.activityIntervalMs"));

        var n = s.Network;
        var network = new NetworkSettings(
            Between(n.PingCount, MinimumPingCount, MaximumPingCount, "network.pingCount"),
            Between(n.PingIntervalMs, MinimumPingIntervalMs, MaximumPingIntervalMs, "network.pingIntervalMs"),
            Between(n.PingTimeoutMs, MinimumPingTimeoutMs, MaximumPingTimeoutMs, "network.pingTimeoutMs"),
            NetworkHost(n.Host, warnings));

        if (warnings.Count == 0 && network == n)
            return new(s, []);
        return new(s with
        {
            Monitoring = monitoring, Detectors = detectors, Benchmark = benchmark, Logging = logging, Journal = journal,
            Diagnostics = diagnostics, Network = network,
        }, warnings);
    }

    private static string? NetworkHost(string? host, List<ConfigWarning> warnings)
    {
        var trimmed = host?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        if (Uri.CheckHostName(trimmed) != UriHostNameType.Unknown)
            return trimmed;
        warnings.Add(new(ConfigWarningKind.InvalidValue, ConfigFiles.Settings, $"network.host: '{trimmed}' is not a host name or IP address"));
        return null;
    }
}
