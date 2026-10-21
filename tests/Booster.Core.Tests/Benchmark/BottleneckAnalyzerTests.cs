using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Metrics;
using Booster.Core.Sensors;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.Benchmark;

public class BottleneckAnalyzerTests
{
    private const string Source = "test";

    private static readonly ThrottleAssessment NoThrottle = new(30, 10, true, []);

    private static readonly BottleneckInput Healthy = new(
        new FrameMetrics(6000, 60_000, 100, 80, 10, 13, 15),
        new FrameStats(6000, GpuBusyShare: 0.8, CpuBusyShare: 0.6, SyncedShare: 0, TypicalSyncInterval: 0),
        new StutterResult(0, 0, 0, 0, 0.5),
        Known(144),
        Telemetry());

    private static Reading<double> Known(double value) => Reading<double>.Known(value, Source);

    private static Reading<double> Value(double? value) => value is { } v ? Known(v) : Reading<double>.Unknown(Source);

    private static SeriesTelemetry Telemetry(double? gpuUtil = 85, double? thread = 60, double? system = 40, double? vramMedian = 50,
        double? vramPeak = 60, double? available = 40, double? commit = 50, double? pageReads = 5, double? diskBusy = 10,
        double? diskLatency = 2, ThrottleAssessment? throttle = null, bool noThrottleData = false, double? dpcPeak = 0.5,
        double? interruptPeak = 0.3) =>
        new(30, Reading<string>.Known("GPU", Source), Reading<string>.Known("1.0", Source), Value(gpuUtil), Value(vramMedian), Value(vramPeak),
            Known(70), Value(thread), Known(20), Value(system), Value(available), Value(commit), Value(pageReads), Value(diskBusy),
            Value(diskLatency), [], noThrottleData ? null : throttle ?? NoThrottle,
            DpcTimeMedianPercent: Value(dpcPeak / 2), DpcTimePeakPercent: Value(dpcPeak),
            InterruptTimeMedianPercent: Value(interruptPeak / 2), InterruptTimePeakPercent: Value(interruptPeak));

    private static BottleneckInput With(FrameStats? frames = null, SeriesTelemetry? telemetry = null, FrameMetrics? metrics = null,
        double? stuttersPerMinute = null, Reading<double>? refresh = null) =>
        Healthy with
        {
            Frames = frames ?? Healthy.Frames,
            Telemetry = telemetry ?? Healthy.Telemetry,
            Metrics = metrics ?? Healthy.Metrics,
            Stutter = stuttersPerMinute is { } rate ? Healthy.Stutter with { StutterCount = 1, StuttersPerMinute = rate } : Healthy.Stutter,
            RefreshRateHz = refresh ?? Healthy.RefreshRateHz,
        };

    private static BottleneckAssessment Analyze(BottleneckInput input) =>
        BottleneckAnalyzer.Analyze(input, FakeConfiguration.DefaultThresholds.Bottleneck);

    private static AnalysisConfidence? ConfidenceOf(BottleneckAssessment assessment, BottleneckClass bottleneck) =>
        assessment.Findings.SingleOrDefault(f => f.Class == bottleneck)?.Confidence;

    private static BottleneckFinding Finding(BottleneckAssessment assessment, BottleneckClass bottleneck) =>
        Assert.Single(assessment.Findings, f => f.Class == bottleneck);

    [Fact]
    public void Healthy_series_has_no_findings_and_normal_gpu_load()
    {
        var assessment = Analyze(Healthy);

        Assert.Empty(assessment.Findings);
        Assert.Equal(GpuLoadState.Normal, assessment.GpuLoad);
        Assert.Empty(assessment.MissingData);
    }

    [Fact]
    public void Gpu_busy_share_and_utilization_high_is_gpu_limited_high()
    {
        var assessment = Analyze(With(Healthy.Frames with { GpuBusyShare = 0.95 }, Telemetry(gpuUtil: 98)));

        Assert.Equal(AnalysisConfidence.High, ConfidenceOf(assessment, BottleneckClass.GpuLimited));
        Assert.Equal(GpuLoadState.Saturated, assessment.GpuLoad);
        Assert.All(Finding(assessment, BottleneckClass.GpuLimited).Evidence, e => Assert.True(e.Supports));
    }

    [Fact]
    public void Gpu_busy_share_high_without_utilization_is_gpu_limited_medium()
    {
        var assessment = Analyze(With(Healthy.Frames with { GpuBusyShare = 0.95 }, Telemetry(gpuUtil: null)));

        Assert.Equal(AnalysisConfidence.Medium, ConfidenceOf(assessment, BottleneckClass.GpuLimited));
        Assert.Equal(GpuLoadState.Saturated, assessment.GpuLoad);
        Assert.Contains(EvidenceKind.GpuUtilization, assessment.MissingData);
    }

    [Fact]
    public void Gpu_utilization_alone_is_gpu_limited_low()
    {
        var assessment = Analyze(With(Healthy.Frames with { GpuBusyShare = null }, Telemetry(gpuUtil: 97)));

        Assert.Equal(AnalysisConfidence.Low, ConfidenceOf(assessment, BottleneckClass.GpuLimited));
        Assert.Equal(GpuLoadState.Normal, assessment.GpuLoad);
    }

    [Fact]
    public void Contradicting_gpu_signals_are_gpu_limited_low_with_evidence_against()
    {
        var assessment = Analyze(With(Healthy.Frames with { GpuBusyShare = 0.95 }, Telemetry(gpuUtil: 50)));

        var finding = Finding(assessment, BottleneckClass.GpuLimited);
        Assert.Equal(AnalysisConfidence.Low, finding.Confidence);
        Assert.False(Assert.Single(finding.Evidence, e => e.Kind == EvidenceKind.GpuUtilization).Supports);
        Assert.True(Assert.Single(finding.Evidence, e => e.Kind == EvidenceKind.GpuBusyFrameShare).Supports);
    }

    [Fact]
    public void Average_cpu_low_but_one_thread_saturated_is_cpu_limited_high()
    {
        var assessment = Analyze(With(Healthy.Frames with { GpuBusyShare = 0.5 }, Telemetry(gpuUtil: 50, thread: 97, system: 30)));

        var finding = Finding(assessment, BottleneckClass.CpuLimited);
        Assert.Equal(AnalysisConfidence.High, finding.Confidence);
        Assert.Equal(GpuLoadState.Underloaded, assessment.GpuLoad);
        Assert.True(Assert.Single(finding.Evidence, e => e.Kind == EvidenceKind.BusiestThread).Supports);
        Assert.False(Assert.Single(finding.Evidence, e => e.Kind == EvidenceKind.SystemCpu).Supports);
    }

    [Fact]
    public void Busy_thread_without_gpu_data_is_cpu_limited_medium()
    {
        var assessment = Analyze(With(Healthy.Frames with { GpuBusyShare = null }, Telemetry(gpuUtil: null, thread: 95)));

        Assert.Equal(AnalysisConfidence.Medium, ConfidenceOf(assessment, BottleneckClass.CpuLimited));
        Assert.Equal(GpuLoadState.Unknown, assessment.GpuLoad);
    }

    [Fact]
    public void Cpu_busy_share_with_idle_gpu_and_no_thread_data_is_cpu_limited_low_with_limiter_caveat()
    {
        var assessment = Analyze(With(Healthy.Frames with { GpuBusyShare = 0.5, CpuBusyShare = 0.95 },
            Telemetry(gpuUtil: 50, thread: null, system: null)));

        var finding = Finding(assessment, BottleneckClass.CpuLimited);
        Assert.Equal(AnalysisConfidence.Low, finding.Confidence);
        Assert.Equal(BottleneckAnalyzer.LimiterDetail, Assert.Single(finding.Evidence, e => e.Kind == EvidenceKind.CpuBusyFrameShare).Detail);
        Assert.Contains(EvidenceKind.BusiestThread, assessment.MissingData);
    }

    [Fact]
    public void High_cpu_busy_share_with_idle_gpu_and_idle_threads_is_not_cpu_limited()
    {
        var assessment = Analyze(With(Healthy.Frames with { GpuBusyShare = 0.05, CpuBusyShare = 0.99 },
            Telemetry(gpuUtil: 5, thread: 20, system: 10), metrics: Healthy.Metrics with { FrameTimeP95Ms = 11, FrameTimeP99Ms = 12 }));

        Assert.Null(ConfidenceOf(assessment, BottleneckClass.CpuLimited));
        var external = Finding(assessment, BottleneckClass.ExternalLimit);
        Assert.Equal(AnalysisConfidence.Medium, external.Confidence);
        Assert.Equal(BottleneckAnalyzer.LimiterDetail, Assert.Single(external.Evidence, e => e.Kind == EvidenceKind.UniformFrameTimes).Detail);
    }

    [Fact]
    public void Vsync_at_refresh_is_external_limit_high()
    {
        var assessment = Analyze(With(Healthy.Frames with { SyncedShare = 1.0, TypicalSyncInterval = 1 },
            metrics: Healthy.Metrics with { AverageFps = 59.9 }, refresh: Known(60)));

        Assert.Equal(AnalysisConfidence.High, ConfidenceOf(assessment, BottleneckClass.ExternalLimit));
    }

    [Fact]
    public void Half_refresh_with_sync_interval_two_is_external_limit()
    {
        var assessment = Analyze(With(Healthy.Frames with { SyncedShare = 1.0, TypicalSyncInterval = 2 },
            metrics: Healthy.Metrics with { AverageFps = 30 }, refresh: Known(60)));

        Assert.Equal(AnalysisConfidence.High, ConfidenceOf(assessment, BottleneckClass.ExternalLimit));
    }

    [Fact]
    public void Fps_at_refresh_without_sync_data_is_external_limit_medium()
    {
        var assessment = Analyze(With(Healthy.Frames with { SyncedShare = null, TypicalSyncInterval = null },
            metrics: Healthy.Metrics with { AverageFps = 143.5 }));

        Assert.Equal(AnalysisConfidence.Medium, ConfidenceOf(assessment, BottleneckClass.ExternalLimit));
        Assert.Contains(EvidenceKind.SyncedFrames, assessment.MissingData);
    }

    [Fact]
    public void Missing_refresh_and_busy_columns_reported_as_missing_data()
    {
        var assessment = Analyze(With(Healthy.Frames with { GpuBusyShare = null, CpuBusyShare = null, SyncedShare = 1.0, TypicalSyncInterval = 1 },
            metrics: Healthy.Metrics with { AverageFps = 60 }, refresh: Reading<double>.Unknown(Source)));

        Assert.Null(ConfidenceOf(assessment, BottleneckClass.GpuLimited));
        Assert.Null(ConfidenceOf(assessment, BottleneckClass.ExternalLimit));
        Assert.Contains(EvidenceKind.FpsAtRefreshRate, assessment.MissingData);
        Assert.Contains(EvidenceKind.GpuBusyFrameShare, assessment.MissingData);
        Assert.Contains(EvidenceKind.CpuBusyFrameShare, assessment.MissingData);
    }

    [Theory]
    [InlineData(ThrottleConfidence.Low, AnalysisConfidence.Low)]
    [InlineData(ThrottleConfidence.Medium, AnalysisConfidence.Medium)]
    [InlineData(ThrottleConfidence.High, AnalysisConfidence.High)]
    public void Throttle_confidence_maps_directly(ThrottleConfidence throttle, AnalysisConfidence expected)
    {
        var findings = new[]
        {
            new ThrottleFinding(ThrottleSignal.CpuPerformanceLimited, ThrottleConfidence.Low, null, 5, 30, Known(20)),
            new ThrottleFinding(ThrottleSignal.GpuDriverThermal, throttle, "GPU", 12, 30, Known(84)),
        };

        var assessment = Analyze(With(telemetry: Telemetry(throttle: NoThrottle with { Findings = findings })));

        var finding = Finding(assessment, BottleneckClass.ThermalThrottling);
        Assert.Equal(expected, finding.Confidence);
        Assert.Equal(2, finding.Evidence.Count(e => e.Kind == EvidenceKind.Throttle));
    }

    [Fact]
    public void Missing_throttle_assessment_is_missing_data()
    {
        var assessment = Analyze(With(telemetry: Telemetry(noThrottleData: true)));

        Assert.Null(ConfidenceOf(assessment, BottleneckClass.ThermalThrottling));
        Assert.Equal([EvidenceKind.Throttle], assessment.MissingData);
    }

    [Fact]
    public void Vram_median_high_is_video_memory_pressure_medium()
    {
        var assessment = Analyze(With(telemetry: Telemetry(vramMedian: 93, vramPeak: 97)));

        Assert.Equal(AnalysisConfidence.Medium, ConfidenceOf(assessment, BottleneckClass.VideoMemoryPressure));
    }

    [Fact]
    public void Vram_peak_only_is_video_memory_pressure_low()
    {
        var assessment = Analyze(With(telemetry: Telemetry(vramMedian: 70, vramPeak: 92)));

        Assert.Equal(AnalysisConfidence.Low, ConfidenceOf(assessment, BottleneckClass.VideoMemoryPressure));
    }

    [Fact]
    public void Low_memory_with_page_reads_is_memory_pressure_high()
    {
        var assessment = Analyze(With(telemetry: Telemetry(available: 5, pageReads: 300)));

        Assert.Equal(AnalysisConfidence.High, ConfidenceOf(assessment, BottleneckClass.MemoryPressure));
    }

    [Fact]
    public void Low_memory_and_high_commit_without_page_reads_is_memory_pressure_medium()
    {
        var assessment = Analyze(With(telemetry: Telemetry(available: 5, commit: 95, pageReads: 10)));

        Assert.Equal(AnalysisConfidence.Medium, ConfidenceOf(assessment, BottleneckClass.MemoryPressure));
    }

    [Fact]
    public void Single_memory_signal_is_memory_pressure_low()
    {
        Assert.Equal(AnalysisConfidence.Low, ConfidenceOf(Analyze(With(telemetry: Telemetry(commit: 95))), BottleneckClass.MemoryPressure));
        Assert.Equal(AnalysisConfidence.Low, ConfidenceOf(Analyze(With(telemetry: Telemetry(available: 8))), BottleneckClass.MemoryPressure));
    }

    [Fact]
    public void Page_reads_alone_are_not_memory_pressure()
    {
        Assert.Null(ConfidenceOf(Analyze(With(telemetry: Telemetry(pageReads: 500))), BottleneckClass.MemoryPressure));
    }

    [Fact]
    public void Busy_disk_with_stutters_is_storage_medium()
    {
        var assessment = Analyze(With(telemetry: Telemetry(diskBusy: 95), stuttersPerMinute: 8));

        Assert.Equal(AnalysisConfidence.Medium, ConfidenceOf(assessment, BottleneckClass.Storage));
    }

    [Fact]
    public void Slow_disk_without_stutters_is_storage_low()
    {
        var assessment = Analyze(With(telemetry: Telemetry(diskLatency: 80)));

        Assert.Equal(AnalysisConfidence.Low, ConfidenceOf(assessment, BottleneckClass.Storage));
    }

    [Fact]
    public void High_dpc_without_stutters_is_low_driver_latency()
    {
        var assessment = Analyze(With(telemetry: Telemetry(dpcPeak: 3)));

        Assert.Equal(AnalysisConfidence.Low, ConfidenceOf(assessment, BottleneckClass.DriverLatency));
        var finding = Finding(assessment, BottleneckClass.DriverLatency);
        Assert.Equal([(EvidenceKind.DpcTime, true), (EvidenceKind.InterruptTime, false), (EvidenceKind.StutterRate, false)],
            finding.Evidence.Select(e => (e.Kind, e.Supports)));
        Assert.Equal(2, finding.Evidence[0].Threshold);
        Assert.Equal(BottleneckAnalyzer.PeakDetail, finding.Evidence[0].Detail);
    }

    [Fact]
    public void High_interrupt_with_stutters_is_medium()
    {
        var assessment = Analyze(With(telemetry: Telemetry(interruptPeak: 4), stuttersPerMinute: 8));

        Assert.Equal(AnalysisConfidence.Medium, ConfidenceOf(assessment, BottleneckClass.DriverLatency));
        Assert.Contains(Finding(assessment, BottleneckClass.DriverLatency).Evidence, e => e is { Kind: EvidenceKind.InterruptTime, Supports: true });
    }

    [Fact]
    public void Driver_latency_is_never_high()
    {
        var assessment = Analyze(With(telemetry: Telemetry(dpcPeak: 20, interruptPeak: 20), stuttersPerMinute: 30));

        Assert.Equal(AnalysisConfidence.Medium, ConfidenceOf(assessment, BottleneckClass.DriverLatency));
    }

    [Fact]
    public void Latency_below_threshold_gives_no_finding()
    {
        var assessment = Analyze(With(telemetry: Telemetry(dpcPeak: 1.9, interruptPeak: 1.9), stuttersPerMinute: 8));

        Assert.Null(ConfidenceOf(assessment, BottleneckClass.DriverLatency));
    }

    [Fact]
    public void Missing_latency_counters_are_listed()
    {
        var assessment = Analyze(With(telemetry: Telemetry() with
        {
            DpcTimeMedianPercent = default, DpcTimePeakPercent = default, InterruptTimeMedianPercent = default, InterruptTimePeakPercent = default,
        }));

        Assert.Equal([EvidenceKind.DpcTime, EvidenceKind.InterruptTime], assessment.MissingData);
        Assert.Null(ConfidenceOf(assessment, BottleneckClass.DriverLatency));
    }

    [Fact]
    public void Long_tail_with_stutters_is_frame_pacing_high()
    {
        var assessment = Analyze(With(metrics: Healthy.Metrics with { FrameTimeP99Ms = 25 }, stuttersPerMinute: 8));

        Assert.Equal(AnalysisConfidence.High, ConfidenceOf(assessment, BottleneckClass.FramePacing));
    }

    [Fact]
    public void Stutters_alone_are_frame_pacing_medium()
    {
        var assessment = Analyze(With(stuttersPerMinute: 8));

        Assert.Equal(AnalysisConfidence.Medium, ConfidenceOf(assessment, BottleneckClass.FramePacing));
        Assert.Null(ConfidenceOf(assessment, BottleneckClass.Storage));
    }

    [Fact]
    public void Findings_sorted_by_confidence()
    {
        var assessment = Analyze(With(Healthy.Frames with { GpuBusyShare = 0.95 },
            Telemetry(gpuUtil: 98, vramMedian: 95, vramPeak: 98, commit: 95), stuttersPerMinute: 8));

        Assert.Equal(
            [
                (BottleneckClass.GpuLimited, AnalysisConfidence.High),
                (BottleneckClass.VideoMemoryPressure, AnalysisConfidence.Medium),
                (BottleneckClass.FramePacing, AnalysisConfidence.Medium),
                (BottleneckClass.MemoryPressure, AnalysisConfidence.Low),
            ],
            assessment.Findings.Select(f => (f.Class, f.Confidence)));
    }
}
