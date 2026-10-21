using Booster.Core.Configuration;

namespace Booster.Core.Tests.Configuration;

public class SettingsNormalizerTests
{
    private static readonly BenchmarkSettings DefaultBenchmark = new(3, 30, 10, 10, true, 100, 300);
    private static readonly DiagnosticsSettings DefaultDiagnostics = new(14, 500, 10, 2000);
    private static readonly NetworkSettings DefaultNetwork = new(30, 1000, 1000, null);

    private static BoosterSettings Settings(
        int selfUsage = 2000, int gpuSample = 2000, int defaultTimeout = 15000, int driversTimeout = 90000,
        int retainedFiles = 14, int gameDetection = 2000, int retainedSessions = 50,
        int sensorInterval = 2000, int exitConfirmations = 2, BenchmarkSettings? benchmark = null,
        DiagnosticsSettings? diagnostics = null, NetworkSettings? network = null) =>
        new(1,
            new MonitoringSettings(selfUsage, gpuSample, gameDetection, sensorInterval, exitConfirmations),
            new DetectorSettings(defaultTimeout, new Dictionary<string, int> { ["drivers"] = driversTimeout }),
            new PresentMonSettings(null, "BoosterCapture"),
            benchmark ?? DefaultBenchmark,
            new LoggingSettings(retainedFiles),
            new JournalSettings(retainedSessions),
            diagnostics ?? DefaultDiagnostics,
            network ?? DefaultNetwork);

    [Fact]
    public void Self_usage_interval_below_one_second_is_raised_to_one_second()
    {
        var result = SettingsNormalizer.Normalize(Settings(selfUsage: 200));

        Assert.Equal(1000, result.Value.Monitoring.SelfUsageIntervalMs);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(ConfigWarningKind.ValueClamped, warning.Kind);
        Assert.Equal("settings.json", warning.File);
        Assert.Contains("selfUsageIntervalMs", warning.Detail);
    }

    [Fact]
    public void Gpu_engine_interval_below_one_second_is_raised_to_one_second()
    {
        var result = SettingsNormalizer.Normalize(Settings(gpuSample: 500));

        Assert.Equal(1000, result.Value.Monitoring.GpuEngineSampleIntervalMs);
        Assert.Contains("gpuEngineSampleIntervalMs", Assert.Single(result.Warnings).Detail);
    }

    [Fact]
    public void Game_detection_interval_below_one_second_is_raised_to_one_second()
    {
        var result = SettingsNormalizer.Normalize(Settings(gameDetection: 500));

        Assert.Equal(1000, result.Value.Monitoring.GameDetectionIntervalMs);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(ConfigWarningKind.ValueClamped, warning.Kind);
        Assert.Contains("gameDetectionIntervalMs", warning.Detail);
    }

    [Fact]
    public void Sensor_interval_below_one_second_is_raised_to_one_second()
    {
        var result = SettingsNormalizer.Normalize(Settings(sensorInterval: 500));

        Assert.Equal(1000, result.Value.Monitoring.SensorIntervalMs);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(ConfigWarningKind.ValueClamped, warning.Kind);
        Assert.Contains("sensorIntervalMs", warning.Detail);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(11, 10)]
    public void Game_exit_confirmation_count_outside_one_to_ten_is_clamped(int configured, int expected)
    {
        var result = SettingsNormalizer.Normalize(Settings(exitConfirmations: configured));

        Assert.Equal(expected, result.Value.Monitoring.GameExitConfirmationCount);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(ConfigWarningKind.ValueClamped, warning.Kind);
        Assert.Contains("gameExitConfirmationCount", warning.Detail);
    }

    [Fact]
    public void Retained_session_count_below_one_is_raised_to_one()
    {
        var result = SettingsNormalizer.Normalize(Settings(retainedSessions: 0));

        Assert.Equal(1, result.Value.Journal.RetainedSessionCount);
        Assert.Contains("retainedSessionCount", Assert.Single(result.Warnings).Detail);
    }

    [Fact]
    public void Valid_settings_are_unchanged()
    {
        var input = Settings();

        var result = SettingsNormalizer.Normalize(input);

        Assert.Equal(2000, result.Value.Monitoring.SelfUsageIntervalMs);
        Assert.Equal(input.Monitoring, result.Value.Monitoring);
        Assert.Equal(input.Benchmark, result.Value.Benchmark);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData(0, 90000, 14, "defaultTimeoutMs")]
    [InlineData(15000, -5, 14, "timeoutsMs.drivers")]
    [InlineData(15000, 90000, 0, "retainedFileCount")]
    public void Values_that_would_break_the_app_are_raised_to_minimum(
        int defaultTimeout, int driversTimeout, int retainedFiles, string expectedKey)
    {
        var result = SettingsNormalizer.Normalize(Settings(
            defaultTimeout: defaultTimeout, driversTimeout: driversTimeout, retainedFiles: retainedFiles));

        var warning = Assert.Single(result.Warnings);
        Assert.Equal(ConfigWarningKind.ValueClamped, warning.Kind);
        Assert.Contains(expectedKey, warning.Detail);
        Assert.True(result.Value.Detectors.TimeoutFor("cpu") >= TimeSpan.FromSeconds(1));
        Assert.True(result.Value.Detectors.TimeoutFor("drivers") >= TimeSpan.FromSeconds(1));
        Assert.True(result.Value.Logging.RetainedFileCount >= 1);
    }

    [Fact]
    public void Benchmark_values_below_range_are_clamped_with_warning()
    {
        var result = SettingsNormalizer.Normalize(Settings(benchmark: new BenchmarkSettings(
            RunCount: 0, RunSeconds: 5, PauseSeconds: -1, StartDelaySeconds: 0, SoundCues: true, RetainedSeriesCount: 0,
            SessionCaptureSeconds: 10)));

        Assert.Equal(new BenchmarkSettings(1, 10, 0, 3, true, 1, 30), result.Value.Benchmark);
        Assert.Equal(
            [
                "benchmark.runCount: 0 -> 1", "benchmark.runSeconds: 5 -> 10", "benchmark.pauseSeconds: -1 -> 0",
                "benchmark.startDelaySeconds: 0 -> 3", "benchmark.retainedSeriesCount: 0 -> 1", "benchmark.sessionCaptureSeconds: 10 -> 30",
            ],
            result.Warnings.Select(w => w.Detail));
        Assert.All(result.Warnings, w => Assert.Equal(ConfigWarningKind.ValueClamped, w.Kind));
    }

    [Fact]
    public void Benchmark_values_above_range_are_clamped_with_warning()
    {
        var result = SettingsNormalizer.Normalize(Settings(benchmark: new BenchmarkSettings(
            RunCount: 11, RunSeconds: 601, PauseSeconds: 301, StartDelaySeconds: 301, SoundCues: false, RetainedSeriesCount: 100000,
            SessionCaptureSeconds: 1801)));

        Assert.Equal(new BenchmarkSettings(10, 600, 300, 300, false, 100000, 1800), result.Value.Benchmark);
        Assert.Equal(5, result.Warnings.Count);
    }

    [Fact]
    public void Diagnostics_values_are_clamped()
    {
        var low = SettingsNormalizer.Normalize(Settings(diagnostics: new DiagnosticsSettings(
            StabilityDays: 0, MaxStabilityEvents: 1, TopProcessCount: 1, ActivityIntervalMs: 100)));

        Assert.Equal(new DiagnosticsSettings(1, 10, 3, 1000), low.Value.Diagnostics);
        Assert.Equal(
            [
                "diagnostics.stabilityDays: 0 -> 1", "diagnostics.maxStabilityEvents: 1 -> 10", "diagnostics.topProcessCount: 1 -> 3",
                "diagnostics.activityIntervalMs: 100 -> 1000",
            ],
            low.Warnings.Select(w => w.Detail));
        Assert.All(low.Warnings, w => Assert.Equal((ConfigWarningKind.ValueClamped, ConfigFiles.Settings), (w.Kind, w.File)));

        var high = SettingsNormalizer.Normalize(Settings(diagnostics: new DiagnosticsSettings(365, 100_000, 100, 60_000)));

        Assert.Equal(new DiagnosticsSettings(90, 5000, 50, 60_000), high.Value.Diagnostics);
        Assert.Equal(3, high.Warnings.Count);
    }

    [Fact]
    public void Network_values_are_clamped()
    {
        var low = SettingsNormalizer.Normalize(Settings(network: new NetworkSettings(PingCount: 1, PingIntervalMs: 50, PingTimeoutMs: 10, null)));

        Assert.Equal(new NetworkSettings(5, 200, 100, null), low.Value.Network);
        Assert.Equal(["network.pingCount: 1 -> 5", "network.pingIntervalMs: 50 -> 200", "network.pingTimeoutMs: 10 -> 100"],
            low.Warnings.Select(w => w.Detail));

        var high = SettingsNormalizer.Normalize(Settings(network: new NetworkSettings(1000, 20_000, 9000, null)));

        Assert.Equal(new NetworkSettings(600, 10_000, 5000, null), high.Value.Network);
        Assert.Equal(3, high.Warnings.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_network_host_becomes_null(string host)
    {
        var result = SettingsNormalizer.Normalize(Settings(network: DefaultNetwork with { Host = host }));

        Assert.Null(result.Value.Network.Host);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Network_host_is_trimmed()
    {
        var result = SettingsNormalizer.Normalize(Settings(network: DefaultNetwork with { Host = " example.com " }));

        Assert.Equal("example.com", result.Value.Network.Host);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    public void Ip_address_is_a_valid_network_host(string host)
    {
        var result = SettingsNormalizer.Normalize(Settings(network: DefaultNetwork with { Host = host }));

        Assert.Equal(host, result.Value.Network.Host);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Invalid_network_host_is_dropped_with_warning()
    {
        var result = SettingsNormalizer.Normalize(Settings(network: DefaultNetwork with { Host = "bad host!" }));

        Assert.Null(result.Value.Network.Host);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal((ConfigWarningKind.InvalidValue, ConfigFiles.Settings), (warning.Kind, warning.File));
        Assert.Contains("network.host", warning.Detail);
    }

    [Fact]
    public void Timeout_for_section_uses_override_or_default()
    {
        var detectors = Settings().Detectors;

        Assert.Equal(TimeSpan.FromMilliseconds(90000), detectors.TimeoutFor("drivers"));
        Assert.Equal(TimeSpan.FromMilliseconds(15000), detectors.TimeoutFor("cpu"));
    }
}
