using Booster.Core.Metrics;

namespace Booster.Core.Tests.Metrics;

public class FrameMetricsCalculatorTests
{
    private const double Tolerance = 1e-9;
    private const int MinimumFrames = 300;

    private static double[] Repeat(double value, int count) => Enumerable.Repeat(value, count).ToArray();

    private static FrameMetrics Metrics(FrameMetricsResult result)
    {
        Assert.Equal(FrameMetricsStatus.Ok, result.Status);
        return Assert.IsType<FrameMetrics>(result.Metrics);
    }

    [Fact]
    public void Constant_frame_time_gives_identical_metrics()
    {
        var m = Metrics(FrameMetricsCalculator.Calculate(Repeat(10.0, 300), MinimumFrames));

        Assert.Equal(300, m.FrameCount);
        Assert.Equal(3000, m.DurationMs, Tolerance);
        Assert.Equal(100, m.AverageFps, Tolerance);
        Assert.Equal(100, m.OnePercentLowFps, Tolerance);
        Assert.Equal(10, m.FrameTimeP50Ms, Tolerance);
        Assert.Equal(10, m.FrameTimeP95Ms, Tolerance);
        Assert.Equal(10, m.FrameTimeP99Ms, Tolerance);
    }

    [Fact]
    public void One_percent_low_shows_spikes_that_p99_misses()
    {
        var intervals = Repeat(10.0, 297).Concat(Repeat(40.0, 3)).ToArray();

        var m = Metrics(FrameMetricsCalculator.Calculate(intervals, MinimumFrames));

        Assert.Equal(3090, m.DurationMs, Tolerance);
        Assert.Equal(300000.0 / 3090, m.AverageFps, Tolerance);
        Assert.Equal(10, m.FrameTimeP99Ms, Tolerance);
        Assert.Equal(25, m.OnePercentLowFps, Tolerance);
    }

    [Fact]
    public void One_percent_tail_rounds_frame_count_up()
    {
        // N = 350 → k = ⌈3,5⌉ = 4 самых долгих кадра
        var intervals = Repeat(10.0, 346).Concat(Repeat(20.0, 4)).ToArray();

        var m = Metrics(FrameMetricsCalculator.Calculate(intervals, MinimumFrames));

        Assert.Equal(50, m.OnePercentLowFps, Tolerance);
        Assert.Equal(350000.0 / 3540, m.AverageFps, Tolerance);
    }

    [Fact]
    public void Percentiles_use_nearest_rank_regardless_of_input_order()
    {
        var intervals = Enumerable.Range(1, 1000).Reverse().Select(i => (double)i).ToArray();

        var m = Metrics(FrameMetricsCalculator.Calculate(intervals, MinimumFrames));

        Assert.Equal(500, m.FrameTimeP50Ms, Tolerance);
        Assert.Equal(950, m.FrameTimeP95Ms, Tolerance);
        Assert.Equal(990, m.FrameTimeP99Ms, Tolerance);
        Assert.Equal(1000 / 995.5, m.OnePercentLowFps, Tolerance);
        Assert.Equal(1000000.0 / 500500, m.AverageFps, Tolerance);
    }

    [Fact]
    public void Fewer_frames_than_minimum_is_insufficient_data()
    {
        var result = FrameMetricsCalculator.Calculate(Repeat(10.0, 299), MinimumFrames);

        Assert.Equal(FrameMetricsStatus.InsufficientData, result.Status);
        Assert.Null(result.Metrics);
        Assert.Equal(299, result.ValidIntervals);
    }

    [Fact]
    public void Empty_input_is_insufficient_data()
    {
        var result = FrameMetricsCalculator.Calculate([], MinimumFrames);

        Assert.Equal(FrameMetricsStatus.InsufficientData, result.Status);
        Assert.Equal(0, result.ValidIntervals);
    }

    [Fact]
    public void Invalid_intervals_are_counted_and_excluded()
    {
        var intervals = Repeat(10.0, 300).Concat([0, -5, double.NaN, double.PositiveInfinity]).ToArray();

        var result = FrameMetricsCalculator.Calculate(intervals, MinimumFrames);
        var m = Metrics(result);

        Assert.Equal(4, result.InvalidIntervals);
        Assert.Equal(300, result.ValidIntervals);
        Assert.Equal(100, m.AverageFps, Tolerance);
        Assert.Equal(100, m.OnePercentLowFps, Tolerance);
        Assert.Equal(10, m.FrameTimeP99Ms, Tolerance);
    }

    [Fact]
    public void Minimum_frames_below_one_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameMetricsCalculator.Calculate(Repeat(10.0, 5), 0));
    }
}
