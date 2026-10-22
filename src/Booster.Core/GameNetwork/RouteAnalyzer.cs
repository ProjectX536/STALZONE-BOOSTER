using Booster.Core.Abstractions;
using Booster.Core.SystemHealth;

namespace Booster.Core.GameNetwork;

public sealed record RouteJump(int AfterTtl, int AtTtl, double IncreaseMs);

public static class RouteAnalyzer
{
    private const string Source = "route probes";

    public static Reading<double> HopMedian(RouteHop hop)
    {
        var replies = hop.ProbesMs.Where(p => p.HasValue).Select(p => p!.Value).ToArray();
        return replies.Length == 0
            ? Reading<double>.Unknown(Source, "no replies")
            : Reading<double>.Known(Statistics.Median(replies), Source);
    }

    public static RouteHop? IspHop(RouteTrace trace) =>
        trace.Hops
            .Where(h => HopMedian(h).IsAvailable && h.Scope is AddressScope.Public or AddressScope.Cgnat)
            .OrderBy(h => h.Ttl)
            .FirstOrDefault();

    public static RouteHop? LastResponding(RouteTrace trace) =>
        trace.Hops
            .Where(h => HopMedian(h).IsAvailable)
            .OrderByDescending(h => h.Ttl)
            .FirstOrDefault();

    public static RouteJump? FindJump(RouteTrace trace, double jumpMs)
    {
        var known = trace.Hops
            .Select(h => (h.Ttl, Median: HopMedian(h)))
            .Where(h => h.Median.IsAvailable)
            .Select(h => (h.Ttl, Median: h.Median.Value))
            .OrderBy(h => h.Ttl)
            .ToArray();
        var lastTtl = trace.Hops.Count == 0 ? int.MinValue : trace.Hops.Max(h => h.Ttl);

        for (var i = 1; i < known.Length; i++)
        {
            var rise = known[i].Median - known[i - 1].Median;
            if (rise < jumpMs)
                continue;

            var level = known[i - 1].Median + jumpMs;
            var later = known[(i + 1)..];
            var isDestination = trace.ReachedTarget && known[i].Ttl == lastTtl;
            if (later.All(h => h.Median >= level) && (later.Length > 0 || isDestination))
                return new RouteJump(known[i - 1].Ttl, known[i].Ttl, rise);
        }

        return null;
    }
}
