using Booster.Core.Configuration;

namespace Booster.Core.Tests.Configuration;

public sealed class ThresholdsTests
{
    private static ConfigLoadResult<Thresholds> Load(string? userJson) => ThresholdsNormalizer.Normalize(
        ConfigLoader.Load<Thresholds>(ConfigFiles.Thresholds, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config", ConfigFiles.Thresholds)),
            userJson, Thresholds.CurrentSchemaVersion, []).Value);

    [Fact]
    public void Throttle_defaults_load_from_config()
    {
        var loaded = Load(userJson: null);

        Assert.Empty(loaded.Warnings);
        Assert.Equal(new ThrottleSettings(2, 90, 10, 10, 30), loaded.Value.Throttle);
    }

    [Fact]
    public void Out_of_range_throttle_values_are_clamped_with_warnings()
    {
        var loaded = Load("""{ "schemaVersion": 1, "throttle": { "gpuBusyUtilizationPercent": 30 } }""");

        Assert.Equal(50, loaded.Value.Throttle.GpuBusyUtilizationPercent);
        Assert.Equal(new ThrottleSettings(2, 50, 10, 10, 30), loaded.Value.Throttle);
        var warning = Assert.Single(loaded.Warnings);
        Assert.Equal(new ConfigWarning(ConfigWarningKind.ValueClamped, ConfigFiles.Thresholds, "throttle.gpuBusyUtilizationPercent: 30 -> 50"), warning);
    }

    [Fact]
    public void Fractional_values_are_reported_in_invariant_culture()
    {
        var loaded = Load("""{ "schemaVersion": 1, "throttle": { "temperatureMarginC": 25.5, "clockDropPercent": 0.5 } }""");

        Assert.Equal(20, loaded.Value.Throttle.TemperatureMarginC);
        Assert.Equal(1, loaded.Value.Throttle.ClockDropPercent);
        Assert.Equal(["throttle.temperatureMarginC: 25.5 -> 20", "throttle.clockDropPercent: 0.5 -> 1"], loaded.Warnings.Select(w => w.Detail));
    }

    [Fact]
    public void Session_window_is_never_shorter_than_minimum_samples()
    {
        var loaded = Load("""{ "schemaVersion": 1, "throttle": { "minimumSamples": 1, "sessionWindowSamples": 1 } }""");

        Assert.Equal(2, loaded.Value.Throttle.MinimumSamples);
        Assert.Equal(2, loaded.Value.Throttle.SessionWindowSamples);
        Assert.Equal(["throttle.minimumSamples: 1 -> 2", "throttle.sessionWindowSamples: 1 -> 2"], loaded.Warnings.Select(w => w.Detail));
    }

    [Fact]
    public void Bottleneck_shares_out_of_range_are_clamped()
    {
        var loaded = Load("""{ "schemaVersion": 1, "bottleneck": { "gpuBoundFrameShare": 1.5, "gpuIdleFrameShare": 0.2, "busyThreadPercent": 120 } }""");

        Assert.Equal(1.0, loaded.Value.Bottleneck.GpuBoundFrameShare);
        Assert.Equal(0.5, loaded.Value.Bottleneck.GpuIdleFrameShare);
        Assert.Equal(100, loaded.Value.Bottleneck.BusyThreadPercent);
        Assert.Equal(
            ["bottleneck.gpuBoundFrameShare: 1.5 -> 1", "bottleneck.gpuIdleFrameShare: 0.2 -> 0.5", "bottleneck.busyThreadPercent: 120 -> 100"],
            loaded.Warnings.Select(w => w.Detail));
    }

    [Fact]
    public void Stutter_factor_below_minimum_is_clamped()
    {
        var loaded = Load("""{ "schemaVersion": 1, "stutter": { "factor": 1.0, "windowFrames": 2 } }""");

        Assert.Equal(new StutterSettings(1.5, 8, 5), loaded.Value.Stutter);
        Assert.Equal(["stutter.factor: 1 -> 1.5", "stutter.windowFrames: 2 -> 5"], loaded.Warnings.Select(w => w.Detail));
    }

    [Fact]
    public void Comparison_and_recommendation_values_out_of_range_are_clamped()
    {
        var loaded = Load("""
            { "schemaVersion": 1, "comparison": { "minimumRuns": 0, "minimumChangePercent": -1 },
              "recommendations": { "backgroundCpuPercent": 0 } }
            """);

        Assert.Equal(1, loaded.Value.Comparison.MinimumRuns);
        Assert.Equal(0, loaded.Value.Comparison.MinimumChangePercent);
        Assert.Equal(1, loaded.Value.Recommendations.BackgroundCpuPercent);
        Assert.Equal(
            ["comparison.minimumRuns: 0 -> 1", "comparison.minimumChangePercent: -1 -> 0", "recommendations.backgroundCpuPercent: 0 -> 1"],
            loaded.Warnings.Select(w => w.Detail));
    }

    [Fact]
    public void Memory_and_network_thresholds_are_clamped()
    {
        var loaded = Load("""
            { "schemaVersion": 1, "memory": { "commitPercent": 10 },
              "network": { "gatewayLatencyMs": 5000, "jitterMs": 0, "lossPercent": 200 } }
            """);

        Assert.Equal(new MemoryThresholds(50), loaded.Value.Memory);
        Assert.Equal(new NetworkThresholds(GatewayLatencyMs: 1000, HostLatencyMs: 100, JitterMs: 1, LossPercent: 100), loaded.Value.Network);
        Assert.Equal(
            ["memory.commitPercent: 10 -> 50", "network.gatewayLatencyMs: 5000 -> 1000", "network.jitterMs: 0 -> 1", "network.lossPercent: 200 -> 100"],
            loaded.Warnings.Select(w => w.Detail));
    }

    [Theory]
    [InlineData(0, 1, "startup.minPrivateMemoryMb: 0 -> 1")]
    [InlineData(10000, 4096, "startup.minPrivateMemoryMb: 10000 -> 4096")]
    public void Startup_threshold_is_clamped(double configured, double expected, string warning)
    {
        var loaded = Load($$"""{ "schemaVersion": 1, "startup": { "minPrivateMemoryMb": {{configured}} } }""");

        Assert.Equal(new StartupThresholds(expected), loaded.Value.Startup);
        var only = Assert.Single(loaded.Warnings);
        Assert.Equal(ConfigWarningKind.ValueClamped, only.Kind);
        Assert.Equal(warning, only.Detail);
    }

    [Theory]
    [InlineData(0, 0.1, "bottleneck.dpcTimePercent: 0 -> 0.1")]
    [InlineData(80, 25, "bottleneck.dpcTimePercent: 80 -> 25")]
    public void Latency_thresholds_are_clamped(double configured, double expected, string warning)
    {
        var loaded = Load($$"""{ "schemaVersion": 1, "bottleneck": { "dpcTimePercent": {{configured}} } }""");

        Assert.Equal(expected, loaded.Value.Bottleneck.DpcTimePercent);
        Assert.Equal(2, loaded.Value.Bottleneck.InterruptTimePercent);
        var only = Assert.Single(loaded.Warnings);
        Assert.Equal(warning, only.Detail);
    }

    [Fact]
    public void Values_in_range_are_kept_as_is()
    {
        var loaded = Load("""{ "schemaVersion": 1, "throttle": { "temperatureMarginC": 0, "gpuBusyUtilizationPercent": 100, "sessionWindowSamples": 10 } }""");

        Assert.Empty(loaded.Warnings);
        Assert.Equal(new ThrottleSettings(0, 100, 10, 10, 10), loaded.Value.Throttle);
    }
}
