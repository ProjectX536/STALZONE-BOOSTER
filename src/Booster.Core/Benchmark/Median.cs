namespace Booster.Core.Benchmark;

internal static class Median
{
    /// <summary>Медиана; при чётном числе значений — среднее двух средних. Сортирует <paramref name="values"/> на месте.</summary>
    public static double Of(Span<double> values)
    {
        if (values.IsEmpty)
            throw new ArgumentException("At least one value is required.", nameof(values));

        values.Sort();
        var middle = values.Length / 2;
        return values.Length % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
    }
}
