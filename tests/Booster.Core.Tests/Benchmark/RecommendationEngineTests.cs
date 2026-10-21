using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.Benchmark;

public class RecommendationEngineTests
{
    private const string Source = "test";

    private static readonly MetricSpread Flat = new(100, 100, 100);

    /// <summary>Три ровных прогона по 100 FPS.</summary>
    private static readonly BenchmarkSummary Consistent = new(3, Flat, Flat, Flat, Flat, new MetricSpread(0, 0, 0), 1.0);

    private static readonly BottleneckAssessment Healthy = new([], GpuLoadState.Normal, []);

    private static Reading<double> Known(double value) => Reading<double>.Known(value, Source);

    private static SeriesTelemetry Telemetry(params BackgroundProcess[] background) =>
        new(30, Reading<string>.Known("GPU", Source), Reading<string>.Known("1.0", Source), Known(85), Known(50), Known(60), Known(70),
            Known(60), Known(20), Known(40), Known(40), Known(50), Known(5), Known(10), Known(2), background, null);

    private static RecommendationInput Input(BottleneckAssessment? assessment = null, BenchmarkSummary? summary = null,
        PowerSource power = PowerSource.AcPower, Reading<bool>? vrr = null, SeriesTelemetry? telemetry = null, bool noSummary = false) =>
        new(assessment ?? Healthy, noSummary ? null : summary ?? Consistent, power, vrr ?? Reading<bool>.Known(true, Source),
            telemetry ?? Telemetry());

    private static BottleneckAssessment Assessment(GpuLoadState gpuLoad = GpuLoadState.Normal,
        params (BottleneckClass Class, AnalysisConfidence Confidence)[] findings) =>
        new([.. findings.Select(f => new BottleneckFinding(f.Class, f.Confidence, []))], gpuLoad, []);

    private static BottleneckAssessment Found(BottleneckClass bottleneck, AnalysisConfidence confidence = AnalysisConfidence.Medium) =>
        Assessment(GpuLoadState.Normal, (bottleneck, confidence));

    private static IReadOnlyList<Recommendation> Recommend(RecommendationInput input) =>
        RecommendationEngine.Recommend(input, FakeConfiguration.DefaultThresholds.Comparison, FakeConfiguration.DefaultThresholds.Recommendations);

    private static RecommendationId[] Ids(IReadOnlyList<Recommendation> recommendations) => [.. recommendations.Select(r => r.Id)];

    [Fact]
    public void Healthy_series_gets_only_no_changes_needed()
    {
        Assert.Equal([RecommendationId.NoChangesNeeded], Ids(Recommend(Input())));
    }

    [Fact]
    public void Low_confidence_findings_give_no_bottleneck_recommendations()
    {
        var assessment = Assessment(GpuLoadState.Normal, (BottleneckClass.GpuLimited, AnalysisConfidence.Low),
            (BottleneckClass.CpuLimited, AnalysisConfidence.Low), (BottleneckClass.MemoryPressure, AnalysisConfidence.Low),
            (BottleneckClass.ThermalThrottling, AnalysisConfidence.Low), (BottleneckClass.Storage, AnalysisConfidence.Low),
            (BottleneckClass.ExternalLimit, AnalysisConfidence.Low), (BottleneckClass.FramePacing, AnalysisConfidence.Low));

        Assert.Equal([RecommendationId.NoChangesNeeded], Ids(Recommend(Input(assessment))));
    }

    [Fact]
    public void Gpu_limited_recommends_reducing_gpu_load()
    {
        var recommendation = Assert.Single(Recommend(Input(Found(BottleneckClass.GpuLimited, AnalysisConfidence.High))));

        Assert.Equal(RecommendationId.ReduceGpuLoad, recommendation.Id);
        Assert.Equal(BottleneckClass.GpuLimited, recommendation.Because);
        Assert.Equal(AnalysisConfidence.High, recommendation.Confidence);
    }

    [Fact]
    public void Cpu_limited_recommends_reducing_cpu_load_and_names_busy_background_apps()
    {
        var telemetry = Telemetry(new BackgroundProcess("chrome", 12), new BackgroundProcess("Discord", 5), new BackgroundProcess("svchost", 2));

        var recommendations = Recommend(Input(Found(BottleneckClass.CpuLimited), telemetry: telemetry));

        Assert.Equal([RecommendationId.ReduceCpuLoad, RecommendationId.CloseBackgroundApps], Ids(recommendations));
        Assert.Equal(["chrome", "Discord"], recommendations[1].Arguments);
        Assert.Equal(BottleneckClass.CpuLimited, recommendations[1].Because);
    }

    [Fact]
    public void Cpu_limited_without_busy_background_apps_does_not_suggest_closing_them()
    {
        var recommendations = Recommend(Input(Found(BottleneckClass.CpuLimited), telemetry: Telemetry(new BackgroundProcess("svchost", 2))));

        Assert.Equal([RecommendationId.ReduceCpuLoad], Ids(recommendations));
    }

    [Fact]
    public void Underloaded_gpu_without_cpu_or_external_limit_needs_investigation()
    {
        var recommendations = Recommend(Input(Assessment(GpuLoadState.Underloaded)));

        Assert.Equal([RecommendationId.InvestigateGpuUnderload], Ids(recommendations));
    }

    [Theory]
    [InlineData(BottleneckClass.CpuLimited)]
    [InlineData(BottleneckClass.ExternalLimit)]
    public void Underloaded_gpu_explained_by_cpu_or_limit_needs_no_investigation(BottleneckClass explained)
    {
        var assessment = Assessment(GpuLoadState.Underloaded, (explained, AnalysisConfidence.Medium));

        Assert.DoesNotContain(RecommendationId.InvestigateGpuUnderload, Ids(Recommend(Input(assessment))));
    }

    [Fact]
    public void External_limit_high_is_frame_cap_reached_and_suggests_vrr_when_off()
    {
        var recommendations = Recommend(Input(Found(BottleneckClass.ExternalLimit, AnalysisConfidence.High), vrr: Reading<bool>.Known(false, Source)));

        Assert.Equal([RecommendationId.FrameCapReached, RecommendationId.ConsiderVrr], Ids(recommendations));
    }

    [Fact]
    public void Frame_cap_with_unknown_vrr_does_not_suggest_vrr()
    {
        var recommendations = Recommend(Input(Found(BottleneckClass.ExternalLimit, AnalysisConfidence.High), vrr: Reading<bool>.Unknown(Source)));

        Assert.Equal([RecommendationId.FrameCapReached], Ids(recommendations));
    }

    [Fact]
    public void Video_memory_pressure_of_any_confidence_recommends_reducing_it()
    {
        var recommendations = Recommend(Input(Found(BottleneckClass.VideoMemoryPressure, AnalysisConfidence.Low)));

        Assert.Equal([RecommendationId.ReduceVideoMemory], Ids(recommendations));
    }

    [Fact]
    public void Thermal_throttling_recommends_checking_cooling()
    {
        Assert.Equal([RecommendationId.CheckCooling], Ids(Recommend(Input(Found(BottleneckClass.ThermalThrottling)))));
    }

    [Fact]
    public void Battery_power_always_recommends_connecting_power()
    {
        var recommendations = Recommend(Input(Found(BottleneckClass.GpuLimited, AnalysisConfidence.High), power: PowerSource.Battery));

        Assert.Equal([RecommendationId.ReduceGpuLoad, RecommendationId.ConnectPower], Ids(recommendations));
    }

    [Fact]
    public void Memory_pressure_recommends_freeing_memory()
    {
        Assert.Equal([RecommendationId.FreeMemory], Ids(Recommend(Input(Found(BottleneckClass.MemoryPressure)))));
    }

    [Fact]
    public void Driver_latency_recommends_trace()
    {
        var recommendation = Assert.Single(Recommend(Input(Found(BottleneckClass.DriverLatency))));

        Assert.Equal((RecommendationId.RecordLatencyTrace, BottleneckClass.DriverLatency, AnalysisConfidence.Medium),
            (recommendation.Id, recommendation.Because, recommendation.Confidence));
        Assert.Empty(recommendation.Arguments);
    }

    [Fact]
    public void Low_driver_latency_gives_no_recommendation()
    {
        Assert.Equal([RecommendationId.NoChangesNeeded],
            Ids(Recommend(Input(Found(BottleneckClass.DriverLatency, AnalysisConfidence.Low)))));
    }

    [Fact]
    public void Storage_recommends_checking_background_disk_use()
    {
        Assert.Equal([RecommendationId.CheckBackgroundDisk], Ids(Recommend(Input(Found(BottleneckClass.Storage)))));
    }

    [Fact]
    public void Frame_pacing_recommends_stabilizing_it()
    {
        Assert.Equal([RecommendationId.StabilizeFramePacing], Ids(Recommend(Input(Found(BottleneckClass.FramePacing)))));
    }

    [Fact]
    public void Run_variation_above_threshold_asks_to_repeat_runs()
    {
        var recommendations = Recommend(Input(summary: Consistent with { AverageFpsVariationPercent = 12 }));

        Assert.Equal([RecommendationId.RepeatInconsistentRuns], Ids(recommendations));
    }

    [Fact]
    public void Too_few_valid_runs_need_more_data()
    {
        var recommendations = Recommend(Input(summary: Consistent with { ValidRuns = 1 }));

        Assert.Equal([RecommendationId.NeedMoreData], Ids(recommendations));
    }

    [Fact]
    public void Missing_summary_or_assessment_needs_more_data()
    {
        Assert.Equal([RecommendationId.NeedMoreData], Ids(Recommend(Input(noSummary: true))));
        Assert.Equal([RecommendationId.NeedMoreData], Ids(Recommend(Input() with { Assessment = null })));
    }

    [Fact]
    public void Recommendations_ordered_by_confidence_then_table_order()
    {
        var assessment = Assessment(GpuLoadState.Normal, (BottleneckClass.FramePacing, AnalysisConfidence.Medium),
            (BottleneckClass.VideoMemoryPressure, AnalysisConfidence.Low), (BottleneckClass.Storage, AnalysisConfidence.Medium),
            (BottleneckClass.GpuLimited, AnalysisConfidence.High));

        var recommendations = Recommend(Input(assessment, power: PowerSource.Battery));

        Assert.Equal(
            [
                RecommendationId.ReduceGpuLoad, RecommendationId.CheckBackgroundDisk, RecommendationId.StabilizeFramePacing,
                RecommendationId.ReduceVideoMemory, RecommendationId.ConnectPower,
            ],
            Ids(recommendations));
    }
}
