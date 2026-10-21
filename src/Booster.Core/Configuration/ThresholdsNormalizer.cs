using System.Globalization;

namespace Booster.Core.Configuration;

public static class ThresholdsNormalizer
{
    private const double MinimumTemperatureMarginC = 0;
    private const double MaximumTemperatureMarginC = 20;

    private const double MinimumGpuBusyPercent = 50;
    private const double MaximumGpuBusyPercent = 100;

    private const double MinimumClockDropPercent = 1;
    private const double MaximumClockDropPercent = 90;

    private const int MinimumSampleCount = 2;

    private const double MinimumStutterFactor = 1.5;
    private const double MaximumStutterFactor = 10;

    private const double MaximumStutterExcessMs = 100;

    private const int MinimumStutterWindow = 5;
    private const int MaximumStutterWindow = 600;

    private const double MinimumFrameShare = 0.5;
    private const double MaximumFrameShare = 1.0;

    private const double MinimumLoadPercent = 50;
    private const double MaximumLoadPercent = 100;

    private const double MinimumRefreshTolerancePercent = 0.5;
    private const double MaximumRefreshTolerancePercent = 20;

    private const double MinimumUniformRatio = 1.0;
    private const double MaximumUniformRatio = 2.0;

    private const double MinimumAvailableMemoryPercent = 1;
    private const double MaximumAvailableMemoryPercent = 50;

    private const double MinimumRateThreshold = 1;

    private const double MinimumPercentileRatio = 1.1;

    private const double MinimumLatencyPercent = 0.1;
    private const double MaximumLatencyPercent = 25;

    private const int MinimumComparisonRuns = 1;
    private const int MaximumComparisonRuns = 10;

    private const double MaximumChangePercent = 50;

    private const double MinimumBackgroundCpuPercent = 1;

    private const double MinimumNetworkMs = 1;

    private const double MaximumGatewayLatencyMs = 1000;

    private const double MaximumHostLatencyMs = 5000;

    private const double MaximumJitterMs = 1000;

    private const double MinimumStartupPrivateMemoryMb = 1;

    private const double MaximumStartupPrivateMemoryMb = 4096;

    public static ConfigLoadResult<Thresholds> Normalize(Thresholds t)
    {
        ArgumentNullException.ThrowIfNull(t);
        var warnings = new List<ConfigWarning>();

        T Clamp<T>(T value, T minimum, T maximum, string key)
            where T : struct, IComparable<T>, IFormattable
        {
            var clamped = value.CompareTo(minimum) < 0 ? minimum : value.CompareTo(maximum) > 0 ? maximum : value;
            if (clamped.CompareTo(value) != 0)
            {
                warnings.Add(new(ConfigWarningKind.ValueClamped, ConfigFiles.Thresholds,
                    string.Create(CultureInfo.InvariantCulture, $"{key}: {value} -> {clamped}")));
            }
            return clamped;
        }

        double Share(double value, string key) => Clamp(value, MinimumFrameShare, MaximumFrameShare, "bottleneck." + key);
        double Load(double value, string key) => Clamp(value, MinimumLoadPercent, MaximumLoadPercent, "bottleneck." + key);
        double Rate(double value, string key) => Clamp(value, MinimumRateThreshold, double.MaxValue, "bottleneck." + key);

        var s = t.Throttle;
        var minimumSamples = Clamp(s.MinimumSamples, MinimumSampleCount, int.MaxValue, "throttle.minimumSamples");
        var throttle = new ThrottleSettings(
            Clamp(s.TemperatureMarginC, MinimumTemperatureMarginC, MaximumTemperatureMarginC, "throttle.temperatureMarginC"),
            Clamp(s.GpuBusyUtilizationPercent, MinimumGpuBusyPercent, MaximumGpuBusyPercent, "throttle.gpuBusyUtilizationPercent"),
            Clamp(s.ClockDropPercent, MinimumClockDropPercent, MaximumClockDropPercent, "throttle.clockDropPercent"),
            minimumSamples,
            Clamp(s.SessionWindowSamples, minimumSamples, int.MaxValue, "throttle.sessionWindowSamples"));

        var st = t.Stutter;
        var stutter = new StutterSettings(
            Clamp(st.Factor, MinimumStutterFactor, MaximumStutterFactor, "stutter.factor"),
            Clamp(st.MinimumExcessMs, 0, MaximumStutterExcessMs, "stutter.minimumExcessMs"),
            Clamp(st.WindowFrames, MinimumStutterWindow, MaximumStutterWindow, "stutter.windowFrames"));

        var b = t.Bottleneck;
        var bottleneck = new BottleneckSettings(
            Share(b.GpuBoundFrameShare, "gpuBoundFrameShare"),
            Load(b.GpuBusyPercent, "gpuBusyPercent"),
            Share(b.GpuIdleFrameShare, "gpuIdleFrameShare"),
            Load(b.GpuIdlePercent, "gpuIdlePercent"),
            Load(b.BusyThreadPercent, "busyThreadPercent"),
            Load(b.SystemCpuPercent, "systemCpuPercent"),
            Share(b.CpuBoundFrameShare, "cpuBoundFrameShare"),
            Clamp(b.RefreshTolerancePercent, MinimumRefreshTolerancePercent, MaximumRefreshTolerancePercent,
                "bottleneck.refreshTolerancePercent"),
            Share(b.SyncedFrameShare, "syncedFrameShare"),
            Clamp(b.UniformFrameTimeRatio, MinimumUniformRatio, MaximumUniformRatio, "bottleneck.uniformFrameTimeRatio"),
            Load(b.VramUsedPercent, "vramUsedPercent"),
            Clamp(b.AvailableMemoryPercent, MinimumAvailableMemoryPercent, MaximumAvailableMemoryPercent,
                "bottleneck.availableMemoryPercent"),
            Load(b.CommitPercent, "commitPercent"),
            Rate(b.PageReadsPerSecond, "pageReadsPerSecond"),
            Load(b.DiskBusyPercent, "diskBusyPercent"),
            Rate(b.DiskLatencyMs, "diskLatencyMs"),
            Rate(b.StuttersPerMinute, "stuttersPerMinute"),
            Clamp(b.PercentileRatio, MinimumPercentileRatio, double.MaxValue, "bottleneck.percentileRatio"),
            Clamp(b.DpcTimePercent, MinimumLatencyPercent, MaximumLatencyPercent, "bottleneck.dpcTimePercent"),
            Clamp(b.InterruptTimePercent, MinimumLatencyPercent, MaximumLatencyPercent, "bottleneck.interruptTimePercent"));

        var c = t.Comparison;
        var comparison = new ComparisonSettings(
            Clamp(c.MinimumRuns, MinimumComparisonRuns, MaximumComparisonRuns, "comparison.minimumRuns"),
            Clamp(c.MinimumChangePercent, 0, MaximumChangePercent, "comparison.minimumChangePercent"),
            Clamp(c.RunVariationPercent, 0, double.MaxValue, "comparison.runVariationPercent"),
            Clamp(c.MinimumStutterChangePerMinute, 0, double.MaxValue, "comparison.minimumStutterChangePerMinute"));

        var recommendations = new RecommendationSettings(Clamp(t.Recommendations.BackgroundCpuPercent, MinimumBackgroundCpuPercent,
            MaximumLoadPercent, "recommendations.backgroundCpuPercent"));

        var memory = new MemoryThresholds(Clamp(t.Memory.CommitPercent, MinimumLoadPercent, MaximumLoadPercent, "memory.commitPercent"));

        var n = t.Network;
        var network = new NetworkThresholds(
            Clamp(n.GatewayLatencyMs, MinimumNetworkMs, MaximumGatewayLatencyMs, "network.gatewayLatencyMs"),
            Clamp(n.HostLatencyMs, MinimumNetworkMs, MaximumHostLatencyMs, "network.hostLatencyMs"),
            Clamp(n.JitterMs, MinimumNetworkMs, MaximumJitterMs, "network.jitterMs"),
            Clamp(n.LossPercent, 0, 100, "network.lossPercent"));

        var startup = new StartupThresholds(Clamp(t.Startup.MinPrivateMemoryMb, MinimumStartupPrivateMemoryMb,
            MaximumStartupPrivateMemoryMb, "startup.minPrivateMemoryMb"));

        return warnings.Count == 0
            ? new(t, [])
            : new(t with
            {
                Throttle = throttle, Stutter = stutter, Bottleneck = bottleneck, Comparison = comparison, Recommendations = recommendations,
                Memory = memory, Network = network, Startup = startup,
            }, warnings);
    }
}
