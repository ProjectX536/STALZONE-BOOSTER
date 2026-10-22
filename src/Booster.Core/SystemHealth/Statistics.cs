namespace Booster.Core.SystemHealth;

internal static class Statistics
{
    public static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.Order().ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    public static double Mad(IReadOnlyList<double> values)
    {
        var median = Median(values);
        return Median(values.Select(v => Math.Abs(v - median)).ToArray());
    }
}
