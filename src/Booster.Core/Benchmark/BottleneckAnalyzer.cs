using Booster.Core.Abstractions;
using Booster.Core.Configuration;

namespace Booster.Core.Benchmark;

public static class BottleneckAnalyzer
{
    public const string FramesSource = "PresentMon frames";
    public const string LimiterDetail = "a frame rate limiter looks the same";
    public const string PeakDetail = "peak";

    public static BottleneckAssessment Analyze(BottleneckInput input, BottleneckSettings settings)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);

        var signals = new Signals(input, settings);
        var gpu = GpuLimited(signals);
        var findings = new[]
            {
                gpu, CpuLimited(signals), ExternalLimit(signals), ThermalThrottling(input), VideoMemoryPressure(signals),
                MemoryPressure(signals), Storage(signals), DriverLatency(signals), FramePacing(signals),
            }
            .OfType<BottleneckFinding>()
            .OrderByDescending(finding => finding.Confidence)
            .ThenBy(finding => finding.Class)
            .ToArray();

        var gpuLoad = gpu is { Confidence: AnalysisConfidence.High or AnalysisConfidence.Medium } ? GpuLoadState.Saturated
            : signals.GpuUnderloaded ? GpuLoadState.Underloaded
            : signals.GpuShare.IsAvailable || signals.GpuUtilization.IsAvailable ? GpuLoadState.Normal
            : GpuLoadState.Unknown;

        return new BottleneckAssessment(findings, gpuLoad, MissingData(input, signals));
    }

    private static BottleneckFinding? GpuLimited(Signals s)
    {
        var shareHigh = AtLeast(s.GpuShare, s.Settings.GpuBoundFrameShare);
        var utilizationHigh = AtLeast(s.GpuUtilization, s.Settings.GpuBusyPercent);
        AnalysisConfidence? confidence = (s.GpuShare.IsAvailable, s.GpuUtilization.IsAvailable) switch
        {
            (true, true) when shareHigh && utilizationHigh => AnalysisConfidence.High,
            (true, true) when shareHigh != utilizationHigh => AnalysisConfidence.Low,
            (true, false) when shareHigh => AnalysisConfidence.Medium,
            (false, true) when utilizationHigh => AnalysisConfidence.Low,
            _ => null,
        };
        return Finding(BottleneckClass.GpuLimited, confidence,
            Signal(EvidenceKind.GpuBusyFrameShare, s.GpuShare, s.Settings.GpuBoundFrameShare, shareHigh),
            Signal(EvidenceKind.GpuUtilization, s.GpuUtilization, s.Settings.GpuBusyPercent, utilizationHigh));
    }

    private static BottleneckFinding? CpuLimited(Signals s)
    {
        var threadHigh = AtLeast(s.BusiestThread, s.Settings.BusyThreadPercent);
        var systemHigh = AtLeast(s.SystemCpu, s.Settings.SystemCpuPercent);
        var cpuBusyHigh = AtLeast(s.CpuShare, s.Settings.CpuBoundFrameShare);
        var gpuKnown = s.GpuShare.IsAvailable || s.GpuUtilization.IsAvailable;
        var limiterLike = s.GpuUnderloaded && cpuBusyHigh && !s.BusiestThread.IsAvailable;

        AnalysisConfidence? confidence = (threadHigh || systemHigh) switch
        {
            true when s.GpuUnderloaded => AnalysisConfidence.High,
            true when !gpuKnown => AnalysisConfidence.Medium,
            false when limiterLike => AnalysisConfidence.Low,
            _ => null,
        };
        return Finding(BottleneckClass.CpuLimited, confidence,
            Signal(EvidenceKind.BusiestThread, s.BusiestThread, s.Settings.BusyThreadPercent, threadHigh),
            Signal(EvidenceKind.SystemCpu, s.SystemCpu, s.Settings.SystemCpuPercent, systemHigh),
            Signal(EvidenceKind.GpuBusyFrameShare, s.GpuShare, s.Settings.GpuIdleFrameShare, AtMost(s.GpuShare, s.Settings.GpuIdleFrameShare)),
            Signal(EvidenceKind.GpuUtilization, s.GpuUtilization, s.Settings.GpuIdlePercent, AtMost(s.GpuUtilization, s.Settings.GpuIdlePercent)),
            Signal(EvidenceKind.CpuBusyFrameShare, s.CpuShare, s.Settings.CpuBoundFrameShare, cpuBusyHigh, limiterLike ? LimiterDetail : null));
    }

    private static BottleneckFinding? ExternalLimit(Signals s)
    {
        var refresh = s.Input.RefreshRateHz;
        var syncInterval = s.Input.Frames.TypicalSyncInterval is > 0 and var interval ? interval : 1;
        var targetFps = refresh.IsAvailable && refresh.Value > 0 ? refresh.Value / syncInterval : (double?)null;
        var fps = Reading<double>.Known(s.Input.Metrics.AverageFps, FramesSource);
        var atRefresh = targetFps is { } target && Math.Abs(fps.Value - target) <= target * s.Settings.RefreshTolerancePercent / 100;
        var synced = AtLeast(s.Synced, s.Settings.SyncedFrameShare);
        var uniform = AtMost(s.UniformRatio, s.Settings.UniformFrameTimeRatio);
        var limiter = uniform && s.GpuUnderloaded && s.BusiestThread.IsAvailable && s.BusiestThread.Value < s.Settings.BusyThreadPercent;

        AnalysisConfidence? confidence = atRefresh && synced ? AnalysisConfidence.High
            : (atRefresh && !s.Synced.IsAvailable) || limiter ? AnalysisConfidence.Medium
            : null;
        return Finding(BottleneckClass.ExternalLimit, confidence,
            targetFps is null ? null : new Evidence(EvidenceKind.FpsAtRefreshRate, fps, targetFps, atRefresh),
            Signal(EvidenceKind.SyncedFrames, s.Synced, s.Settings.SyncedFrameShare, synced),
            Signal(EvidenceKind.UniformFrameTimes, s.UniformRatio, s.Settings.UniformFrameTimeRatio, uniform, limiter ? LimiterDetail : null));
    }

    private static BottleneckFinding? ThermalThrottling(BottleneckInput input)
    {
        var findings = input.Telemetry.Throttle?.Findings ?? [];
        if (findings.Count == 0)
            return null;
        var confidence = findings.Max(finding => finding.Confidence) switch
        {
            Sensors.ThrottleConfidence.High => AnalysisConfidence.High,
            Sensors.ThrottleConfidence.Medium => AnalysisConfidence.Medium,
            _ => AnalysisConfidence.Low,
        };
        return new BottleneckFinding(BottleneckClass.ThermalThrottling, confidence,
            [.. findings.Select(finding => new Evidence(EvidenceKind.Throttle, finding.Peak, null, true, finding.Signal.ToString()))]);
    }

    private static BottleneckFinding? VideoMemoryPressure(Signals s)
    {
        var median = s.Input.Telemetry.VramUsedMedianPercent;
        var peak = s.Input.Telemetry.VramUsedPeakPercent;
        var medianHigh = AtLeast(median, s.Settings.VramUsedPercent);
        var peakHigh = AtLeast(peak, s.Settings.VramUsedPercent);
        AnalysisConfidence? confidence = medianHigh ? AnalysisConfidence.Medium : peakHigh ? AnalysisConfidence.Low : null;
        return Finding(BottleneckClass.VideoMemoryPressure, confidence,
            Signal(EvidenceKind.VramUsage, median, s.Settings.VramUsedPercent, medianHigh),
            Signal(EvidenceKind.VramUsage, peak, s.Settings.VramUsedPercent, peakHigh, PeakDetail));
    }

    private static BottleneckFinding? MemoryPressure(Signals s)
    {
        var telemetry = s.Input.Telemetry;
        var lowAvailable = AtMost(telemetry.AvailableMemoryMinPercent, s.Settings.AvailableMemoryPercent);
        var highCommit = AtLeast(telemetry.CommitPeakPercent, s.Settings.CommitPercent);
        var pageReads = AtLeast(telemetry.PageReadsPerSecond, s.Settings.PageReadsPerSecond);
        AnalysisConfidence? confidence = (lowAvailable || highCommit) && pageReads ? AnalysisConfidence.High
            : lowAvailable && highCommit ? AnalysisConfidence.Medium
            : lowAvailable || highCommit ? AnalysisConfidence.Low
            : null;
        return Finding(BottleneckClass.MemoryPressure, confidence,
            Signal(EvidenceKind.AvailableMemory, telemetry.AvailableMemoryMinPercent, s.Settings.AvailableMemoryPercent, lowAvailable),
            Signal(EvidenceKind.CommitCharge, telemetry.CommitPeakPercent, s.Settings.CommitPercent, highCommit),
            Signal(EvidenceKind.PageReads, telemetry.PageReadsPerSecond, s.Settings.PageReadsPerSecond, pageReads));
    }

    private static BottleneckFinding? Storage(Signals s)
    {
        var telemetry = s.Input.Telemetry;
        var busy = AtLeast(telemetry.DiskBusyPercent, s.Settings.DiskBusyPercent);
        var slow = AtLeast(telemetry.DiskLatencyMs, s.Settings.DiskLatencyMs);
        AnalysisConfidence? confidence = !(busy || slow) ? null : s.Stuttering ? AnalysisConfidence.Medium : AnalysisConfidence.Low;
        return Finding(BottleneckClass.Storage, confidence,
            Signal(EvidenceKind.DiskBusy, telemetry.DiskBusyPercent, s.Settings.DiskBusyPercent, busy),
            Signal(EvidenceKind.DiskLatency, telemetry.DiskLatencyMs, s.Settings.DiskLatencyMs, slow),
            new Evidence(EvidenceKind.StutterRate, s.StutterRate, s.Settings.StuttersPerMinute, s.Stuttering));
    }

    private static BottleneckFinding? DriverLatency(Signals s)
    {
        var telemetry = s.Input.Telemetry;
        var dpc = AtLeast(telemetry.DpcTimePeakPercent, s.Settings.DpcTimePercent);
        var interrupt = AtLeast(telemetry.InterruptTimePeakPercent, s.Settings.InterruptTimePercent);
        AnalysisConfidence? confidence = !(dpc || interrupt) ? null : s.Stuttering ? AnalysisConfidence.Medium : AnalysisConfidence.Low;
        return Finding(BottleneckClass.DriverLatency, confidence,
            Signal(EvidenceKind.DpcTime, telemetry.DpcTimePeakPercent, s.Settings.DpcTimePercent, dpc, PeakDetail),
            Signal(EvidenceKind.InterruptTime, telemetry.InterruptTimePeakPercent, s.Settings.InterruptTimePercent, interrupt, PeakDetail),
            new Evidence(EvidenceKind.StutterRate, s.StutterRate, s.Settings.StuttersPerMinute, s.Stuttering));
    }

    private static BottleneckFinding? FramePacing(Signals s)
    {
        var longTail = AtLeast(s.TailRatio, s.Settings.PercentileRatio);
        AnalysisConfidence? confidence = longTail && s.Stuttering ? AnalysisConfidence.High
            : longTail || s.Stuttering ? AnalysisConfidence.Medium
            : null;
        return Finding(BottleneckClass.FramePacing, confidence,
            Signal(EvidenceKind.PercentileRatio, s.TailRatio, s.Settings.PercentileRatio, longTail),
            new Evidence(EvidenceKind.StutterRate, s.StutterRate, s.Settings.StuttersPerMinute, s.Stuttering));
    }

    private static IReadOnlyList<EvidenceKind> MissingData(BottleneckInput input, Signals s)
    {
        var telemetry = input.Telemetry;
        var missing = new List<EvidenceKind>();
        void MissingIf(bool absent, EvidenceKind kind)
        {
            if (absent && !missing.Contains(kind))
                missing.Add(kind);
        }

        MissingIf(!s.GpuShare.IsAvailable, EvidenceKind.GpuBusyFrameShare);
        MissingIf(!s.GpuUtilization.IsAvailable, EvidenceKind.GpuUtilization);
        MissingIf(!s.BusiestThread.IsAvailable, EvidenceKind.BusiestThread);
        MissingIf(!s.SystemCpu.IsAvailable, EvidenceKind.SystemCpu);
        MissingIf(!s.CpuShare.IsAvailable, EvidenceKind.CpuBusyFrameShare);
        MissingIf(!input.RefreshRateHz.IsAvailable, EvidenceKind.FpsAtRefreshRate);
        MissingIf(!s.Synced.IsAvailable, EvidenceKind.SyncedFrames);
        MissingIf(!telemetry.VramUsedMedianPercent.IsAvailable, EvidenceKind.VramUsage);
        MissingIf(!telemetry.AvailableMemoryMinPercent.IsAvailable, EvidenceKind.AvailableMemory);
        MissingIf(!telemetry.CommitPeakPercent.IsAvailable, EvidenceKind.CommitCharge);
        MissingIf(!telemetry.PageReadsPerSecond.IsAvailable, EvidenceKind.PageReads);
        MissingIf(!telemetry.DiskBusyPercent.IsAvailable, EvidenceKind.DiskBusy);
        MissingIf(!telemetry.DiskLatencyMs.IsAvailable, EvidenceKind.DiskLatency);
        MissingIf(telemetry.Throttle is null, EvidenceKind.Throttle);
        MissingIf(!telemetry.DpcTimePeakPercent.IsAvailable, EvidenceKind.DpcTime);
        MissingIf(!telemetry.InterruptTimePeakPercent.IsAvailable, EvidenceKind.InterruptTime);
        return missing;
    }

    private static BottleneckFinding? Finding(BottleneckClass bottleneck, AnalysisConfidence? confidence, params Evidence?[] evidence) =>
        confidence is { } known ? new BottleneckFinding(bottleneck, known, [.. evidence.OfType<Evidence>()]) : null;

    private static Evidence? Signal(EvidenceKind kind, Reading<double> value, double threshold, bool supports, string? detail = null) =>
        value.IsAvailable ? new Evidence(kind, value, threshold, supports, detail) : null;

    private static bool AtLeast(Reading<double> reading, double threshold) => reading.IsAvailable && reading.Value >= threshold;

    private static bool AtMost(Reading<double> reading, double threshold) => reading.IsAvailable && reading.Value <= threshold;

    private static Reading<double> FrameValue(double? value, string note) =>
        value is { } known ? Reading<double>.Known(known, FramesSource) : Reading<double>.Unknown(FramesSource, note);

    private sealed class Signals
    {
        public Signals(BottleneckInput input, BottleneckSettings settings)
        {
            Input = input;
            Settings = settings;
            GpuShare = FrameValue(input.Frames.GpuBusyShare, "PresentMon CSV has no GPU busy column");
            CpuShare = FrameValue(input.Frames.CpuBusyShare, "PresentMon CSV has no CPU busy column");
            Synced = FrameValue(input.Frames.SyncedShare, "PresentMon CSV has no SyncInterval column");
            GpuUtilization = input.Telemetry.GpuUtilizationPercent;
            BusiestThread = input.Telemetry.BusiestThreadPercent;
            SystemCpu = input.Telemetry.SystemCpuPercent;

            var metrics = input.Metrics;
            UniformRatio = metrics.FrameTimeP50Ms > 0
                ? Reading<double>.Known(metrics.FrameTimeP95Ms / metrics.FrameTimeP50Ms, FramesSource) : Reading<double>.Unknown(FramesSource);
            TailRatio = metrics.FrameTimeP50Ms > 0
                ? Reading<double>.Known(metrics.FrameTimeP99Ms / metrics.FrameTimeP50Ms, FramesSource) : Reading<double>.Unknown(FramesSource);
            StutterRate = Reading<double>.Known(input.Stutter.StuttersPerMinute, FramesSource);
            Stuttering = input.Stutter.StuttersPerMinute >= settings.StuttersPerMinute;

            var gpuIdle = new List<bool>();
            if (GpuShare.IsAvailable)
                gpuIdle.Add(GpuShare.Value <= settings.GpuIdleFrameShare);
            if (GpuUtilization.IsAvailable)
                gpuIdle.Add(GpuUtilization.Value <= settings.GpuIdlePercent);
            GpuUnderloaded = gpuIdle.Count > 0 && gpuIdle.All(idle => idle);
        }

        public BottleneckInput Input { get; }
        public BottleneckSettings Settings { get; }
        public Reading<double> GpuShare { get; }
        public Reading<double> CpuShare { get; }
        public Reading<double> Synced { get; }
        public Reading<double> GpuUtilization { get; }
        public Reading<double> BusiestThread { get; }
        public Reading<double> SystemCpu { get; }
        public Reading<double> UniformRatio { get; }
        public Reading<double> TailRatio { get; }
        public Reading<double> StutterRate { get; }
        public bool Stuttering { get; }
        public bool GpuUnderloaded { get; }
    }
}
