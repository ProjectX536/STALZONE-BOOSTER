using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Metrics;
using Booster.Core.Sensors;

namespace Booster.Core.Tests.TestSupport;

/// <summary>Серии бенчмарка для тестов хранения; файл подключается и в Booster.Platform.Windows.Tests.</summary>
public static class SampleSeries
{
    public const string Source = "test";

    /// <summary>Серия, в которой заполнены все вложенные записи, перечисления и все статусы <see cref="Reading{T}"/>.</summary>
    public static BenchmarkSeries Full(string id = "20261020-120000-game", int minutes = 0)
    {
        var display = new DisplayMode("Monitor", 2560, 1440, 143.856, false, null);
        var conditions = new BenchmarkConditions("game.exe", Reading<string>.Known("GeForce RTX 4070", Source),
            Reading<string>.Unknown(Source, "driver not reported"), Reading<DisplayMode>.Known(display, Source),
            Reading<string>.Failure("PowerGetActiveScheme", "read failed"), PowerSource.Battery, "Соревновательный",
            new BenchmarkNotes("Высокие", "Город", null, "Новый драйвер"), ["Hardware: Independent Flip"],
            [new BackgroundProcess("browser.exe", 7.5)]);
        var run = new BenchmarkRun(1,
            new FrameMetricsResult(FrameMetricsStatus.Ok, new FrameMetrics(3600, 30000, 120, 80.5, 8.3, 11.1, 14.2), 3599, 1),
            new StutterResult(2, 4, 0.5, 33.3, 0.4), new FrameStats(3600, 0.95, null, 0.1, 1));
        var shortRun = new BenchmarkRun(2, new FrameMetricsResult(FrameMetricsStatus.InsufficientData, null, 10, 0), null,
            new FrameStats(11, null, null, null, null));
        var spread = new MetricSpread(120, 118, 122);
        var summary = new BenchmarkSummary(1, spread, spread, spread, spread, new MetricSpread(4, 4, 4), 3.33);
        var telemetry = new SeriesTelemetry(30, Reading<string>.Known("GeForce RTX 4070", Source), Reading<string>.Known("32.0.15.6094", Source),
            Reading<double>.Known(97.5, Source), Reading<double>.NeedsAdmin(Source), Reading<double>.Unsupported(Source, "no counter"),
            Reading<double>.Known(71, Source), Reading<double>.Known(64, Source), Reading<double>.Known(18, Source), Reading<double>.Known(40, Source),
            Reading<double>.Known(35, Source), Reading<double>.Known(60, Source), Reading<double>.Known(0, Source), Reading<double>.Unknown(),
            Reading<double>.Unknown(), [new BackgroundProcess("browser.exe", 7.5)],
            new ThrottleAssessment(30, 10, true,
                [new ThrottleFinding(ThrottleSignal.GpuDriverPowerLimit, ThrottleConfidence.High, "GeForce RTX 4070", 5, 30, Reading<double>.Unknown())]),
            DpcTimeMedianPercent: Reading<double>.Known(0.3, Source), DpcTimePeakPercent: Reading<double>.Known(2.4, Source),
            InterruptTimeMedianPercent: Reading<double>.Known(0.2, Source), InterruptTimePeakPercent: Reading<double>.Unknown(Source, "first reading"));
        var bottleneck = new BottleneckAssessment(
            [new BottleneckFinding(BottleneckClass.GpuLimited, AnalysisConfidence.High,
                [new Evidence(EvidenceKind.GpuBusyFrameShare, Reading<double>.Known(0.95, "PresentMon frames"), 0.9, true)])],
            GpuLoadState.Saturated, [EvidenceKind.DiskLatency]);
        return new BenchmarkSeries(id, new DateTimeOffset(2026, 10, 20, 12, minutes, 0, TimeSpan.FromHours(3)),
            new BenchmarkSchedule(3, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)), FrameCaptureStatus.Completed,
            null, conditions, [run, shortRun], summary, telemetry, bottleneck,
            [new Recommendation(RecommendationId.CloseBackgroundApps, null, null, ["browser.exe"]),
             new Recommendation(RecommendationId.ReduceGpuLoad, BottleneckClass.GpuLimited, AnalysisConfidence.High, [])],
            @"C:\Data\captures\a.csv");
    }

    /// <summary>Завершённая серия из трёх годных прогонов: средний FPS <paramref name="fps"/> ± 1, 1% low — 70% от него ± 1.</summary>
    public static BenchmarkSeries Completed(string id, DateTimeOffset startedAt, string target = "game.exe", double fps = 100)
    {
        var sample = Full(id);
        var spread = new MetricSpread(10, 9.5, 10.5);
        return sample with
        {
            StartedAt = startedAt,
            Conditions = sample.Conditions with { Target = target },
            Summary = new BenchmarkSummary(3, new MetricSpread(fps, fps - 1, fps + 1), new MetricSpread(fps * 0.7, fps * 0.7 - 1, fps * 0.7 + 1),
                spread, spread, new MetricSpread(1, 1, 1), 2),
        };
    }
}
