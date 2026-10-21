using Booster.Core.Abstractions;
using Booster.Core.Configuration;

namespace Booster.Core.Benchmark;

public enum RecommendationId
{
    ReduceGpuLoad,
    ReduceCpuLoad,
    InvestigateGpuUnderload,
    FrameCapReached,
    ConsiderVrr,
    ReduceVideoMemory,
    CheckCooling,
    ConnectPower,
    FreeMemory,
    CheckBackgroundDisk,
    StabilizeFramePacing,
    CloseBackgroundApps,
    RepeatInconsistentRuns,
    NeedMoreData,
    NoChangesNeeded,
    RecordLatencyTrace,
}

/// <summary>Только совет: Booster ничего не меняет (L0, recommend-only). Arguments — подстановки в текст (имена процессов, числа).</summary>
public sealed record Recommendation(RecommendationId Id, BottleneckClass? Because, AnalysisConfidence? Confidence, IReadOnlyList<string> Arguments);

public sealed record RecommendationInput(BottleneckAssessment? Assessment, BenchmarkSummary? Summary, PowerSource PowerSource,
    Reading<bool> VrrEnabled, SeriesTelemetry Telemetry);

/// <summary>Рекомендации по анализу серии. Правила — docs/ARCHITECTURE.md, раздел 10, «Анализ ограничений».</summary>
public static class RecommendationEngine
{
    /// <summary>По уверенности причины (без причины — в конце), затем в порядке правил.</summary>
    public static IReadOnlyList<Recommendation> Recommend(RecommendationInput input, ComparisonSettings comparison,
        RecommendationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(settings);

        var assessment = input.Assessment;
        var result = new List<Recommendation>();

        BottleneckFinding? Found(BottleneckClass bottleneck, AnalysisConfidence minimum = AnalysisConfidence.Medium) =>
            assessment?.Findings.FirstOrDefault(finding => finding.Class == bottleneck && finding.Confidence >= minimum);

        void Add(RecommendationId id, BottleneckFinding? because = null, IReadOnlyList<string>? arguments = null) =>
            result.Add(new Recommendation(id, because?.Class, because?.Confidence, arguments ?? []));

        if (Found(BottleneckClass.GpuLimited) is { } gpu)
            Add(RecommendationId.ReduceGpuLoad, gpu);

        var cpu = Found(BottleneckClass.CpuLimited);
        if (cpu is not null)
        {
            Add(RecommendationId.ReduceCpuLoad, cpu);
            string[] busy = [.. input.Telemetry.Background.Where(process => process.CpuPercent >= settings.BackgroundCpuPercent)
                .Select(process => process.Name)];
            if (busy.Length > 0)
                Add(RecommendationId.CloseBackgroundApps, cpu, busy);
        }

        if (assessment is { GpuLoad: GpuLoadState.Underloaded } && cpu is null && Found(BottleneckClass.ExternalLimit) is null)
            Add(RecommendationId.InvestigateGpuUnderload);

        if (Found(BottleneckClass.ExternalLimit, AnalysisConfidence.High) is { } limit)
        {
            Add(RecommendationId.FrameCapReached, limit);
            if (input.VrrEnabled is { IsAvailable: true, Value: false })
                Add(RecommendationId.ConsiderVrr, limit);
        }

        if (Found(BottleneckClass.VideoMemoryPressure, AnalysisConfidence.Low) is { } vram)
            Add(RecommendationId.ReduceVideoMemory, vram);
        if (Found(BottleneckClass.ThermalThrottling) is { } throttling)
            Add(RecommendationId.CheckCooling, throttling);
        if (input.PowerSource == PowerSource.Battery)
            Add(RecommendationId.ConnectPower);
        if (Found(BottleneckClass.MemoryPressure) is { } memory)
            Add(RecommendationId.FreeMemory, memory);
        if (Found(BottleneckClass.Storage) is { } storage)
            Add(RecommendationId.CheckBackgroundDisk, storage);
        // Без рывков высокий DPC остаётся находкой без совета: на играбельность он не повлиял.
        if (Found(BottleneckClass.DriverLatency) is { } latency)
            Add(RecommendationId.RecordLatencyTrace, latency);
        if (Found(BottleneckClass.FramePacing) is { } pacing)
            Add(RecommendationId.StabilizeFramePacing, pacing);
        if (input.Summary is { } summary && summary.AverageFpsVariationPercent > comparison.RunVariationPercent)
            Add(RecommendationId.RepeatInconsistentRuns);

        var enoughData = assessment is not null && input.Summary is { } valid && valid.ValidRuns >= comparison.MinimumRuns;
        if (!enoughData)
            Add(RecommendationId.NeedMoreData);
        // «Менять нечего» — только когда сказать больше нечего: иначе совет и «всё хорошо» спорят друг с другом.
        else if (result.Count == 0 && !assessment!.Findings.Any(finding => finding.Confidence >= AnalysisConfidence.Medium))
            Add(RecommendationId.NoChangesNeeded);

        // OrderByDescending устойчива: при равной уверенности остаётся порядок правил.
        return [.. result.OrderByDescending(recommendation => recommendation.Confidence is { } confidence ? (int)confidence : -1)];
    }
}
