using Booster.Core.Abstractions;
using Booster.Core.SystemHealth;

namespace Booster.Core.GameNetwork;

public sealed record LatencyBreakdown(Reading<double> HomeMs, Reading<double> LineMs, Reading<double> RouteMs,
    Reading<double> TotalMs, NetworkTargetKind EndKind, bool Clamped);

public static class LatencyBreakdownCalculator
{
    private const string Source = "ping medians";

    public static LatencyBreakdown Compute(NetworkTargetResult? gateway, NetworkTargetResult? ispHop, NetworkTargetResult end)
    {
        var clamped = false;
        var home = Median(gateway, "no gateway");
        var isp = Median(ispHop, "no ISP hop");
        var total = Median(end, "no replies");

        var line = Difference(isp, home, ref clamped);
        var route = Difference(total, isp, ref clamped);

        return new LatencyBreakdown(home, line, route, total, end.Kind, clamped);
    }

    private static Reading<double> Median(NetworkTargetResult? target, string missingNote)
    {
        if (target is null)
            return Reading<double>.Unknown(Source, missingNote);
        return target.MedianMs.IsAvailable
            ? Reading<double>.Known(target.MedianMs.Value, Source)
            : Reading<double>.Unknown(Source, "no replies");
    }

    private static Reading<double> Difference(Reading<double> later, Reading<double> earlier, ref bool clamped)
    {
        if (!later.IsAvailable || !earlier.IsAvailable)
            return Reading<double>.Unknown(Source, earlier.IsAvailable ? later.Note : earlier.Note);

        var difference = later.Value - earlier.Value;
        if (difference >= 0)
            return Reading<double>.Known(difference, Source);

        clamped = true;
        return Reading<double>.Known(0, Source);
    }
}
