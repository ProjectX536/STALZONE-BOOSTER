using System.Globalization;
using Booster.App.Resources;
using Booster.Core.Benchmark;

namespace Booster.App.ViewModels;

/// <summary>Строка истории серий: дата, игра, изменение и медианы FPS — то, по чему пользователь выбирает серию.</summary>
public sealed record BenchmarkHistoryItem(BenchmarkSeries Series, string StartedAt, string Target, string Change, string AverageFps,
    string OnePercentLow)
{
    public string Id => Series.Id;

    /// <summary>Нет изменения или годных прогонов — «—»: значение не выдумывается.</summary>
    public static BenchmarkHistoryItem From(BenchmarkSeries series, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(series);
        var summary = series.Summary;
        return new BenchmarkHistoryItem(series,
            series.StartedAt.ToLocalTime().ToString("G", culture),
            series.Conditions.Target,
            string.IsNullOrWhiteSpace(series.Conditions.Notes.Change) ? Strings.NoValue : series.Conditions.Notes.Change,
            summary is null ? Strings.NoValue : summary.AverageFps.Median.ToString("0.0", culture),
            summary is null ? Strings.NoValue : summary.OnePercentLowFps.Median.ToString("0.0", culture));
    }
}
