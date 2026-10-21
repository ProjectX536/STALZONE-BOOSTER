using Booster.Core.Abstractions;
using Booster.Core.Inventory;

namespace Booster.Platform.Windows.Monitoring;

public static class GpuAdapterUsageAggregator
{
    public const string Source = @"Performance counters GPU Engine\Utilization Percentage, engtype_3D";
    public const string RenderEngine = "3D";

    public static Reading<AdapterLuid> DominantRenderAdapter(IEnumerable<IReadOnlyList<GpuEngineSample>> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var totals = new List<(AdapterLuid Luid, double Total)>();
        foreach (var sample in samples.SelectMany(s => s))
        {
            if (!string.Equals(sample.EngineType, RenderEngine, StringComparison.OrdinalIgnoreCase))
                continue;
            var index = totals.FindIndex(t => t.Luid == sample.Luid);
            if (index < 0)
                totals.Add((sample.Luid, sample.UtilizationPercent));
            else
                totals[index] = (sample.Luid, totals[index].Total + sample.UtilizationPercent);
        }

        if (totals.Count == 0 || totals.Max(t => t.Total) <= 0)
            return Reading<AdapterLuid>.Unknown(Source, "the process put no load on 3D engines during the capture");

        var best = totals[0];
        foreach (var total in totals)
        {
            if (total.Total > best.Total)
                best = total;
        }
        return Reading<AdapterLuid>.Known(best.Luid, Source);
    }
}
