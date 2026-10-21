using Booster.Core.Benchmark;
using Booster.Core.Configuration;

namespace Booster.Core.Tests.Benchmark;

public class StutterAnalyzerTests
{
    private const double Tolerance = 1e-9;
    private static readonly StutterSettings Settings = new(Factor: 2.0, MinimumExcessMs: 8, WindowFrames: 30);

    private static List<double> Repeat(double ms, int count) => Enumerable.Repeat(ms, count).ToList();

    [Fact]
    public void Even_frames_have_no_stutters()
    {
        var result = StutterAnalyzer.Analyze(Repeat(16.7, 600), Settings);

        Assert.Equal(new StutterResult(0, 0, 0, 0, 0), result);
    }

    [Fact]
    public void Single_long_frame_is_one_stutter()
    {
        var intervals = Repeat(16.7, 600);
        intervals.Insert(300, 50);
        var totalMs = 600 * 16.7 + 50;

        var result = StutterAnalyzer.Analyze(intervals, Settings);

        Assert.Equal(1, result.StutterCount);
        Assert.Equal(50, result.WorstStutterMs);
        Assert.Equal(1 / (totalMs / 60_000), result.StuttersPerMinute, Tolerance);
        Assert.Equal(50 / totalMs * 100, result.StutterTimePercent, Tolerance);
    }

    [Fact]
    public void Doubling_at_high_fps_below_minimum_excess_is_not_stutter()
    {
        // 7 ≥ 2 × 3, но 7 − 3 = 4 мс меньше минимального превышения 8 мс.
        var intervals = Repeat(3, 600);
        intervals.Insert(300, 7);

        Assert.Equal(0, StutterAnalyzer.Analyze(intervals, Settings).StutterCount);
    }

    [Fact]
    public void Scene_change_to_lower_fps_is_not_stutter()
    {
        // На границе медиана соседей (8 + 20) / 2 = 14, а 20 < 2 × 14.
        var intervals = Repeat(8, 300).Concat(Repeat(20, 300)).ToList();

        Assert.Equal(0, StutterAnalyzer.Analyze(intervals, Settings).StutterCount);
    }

    [Fact]
    public void Long_frame_at_run_start_is_detected_with_shifted_window()
    {
        List<double> intervals = [50, .. Repeat(16.7, 600)];

        var result = StutterAnalyzer.Analyze(intervals, Settings);

        Assert.Equal(1, result.StutterCount);
        Assert.Equal(50, result.WorstStutterMs);
    }

    [Fact]
    public void Long_frame_at_run_end_is_detected_with_shifted_window()
    {
        List<double> intervals = [.. Repeat(16.7, 600), 50];

        Assert.Equal(1, StutterAnalyzer.Analyze(intervals, Settings).StutterCount);
    }

    [Fact]
    public void Run_shorter_than_window_uses_all_other_frames()
    {
        var intervals = Repeat(16.7, 10);
        intervals[4] = 50;

        Assert.Equal(1, StutterAnalyzer.Analyze(intervals, Settings).StutterCount);
    }

    [Fact]
    public void Mean_frame_to_frame_delta_measures_unevenness_without_stutters()
    {
        var intervals = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? 10.0 : 14.0).ToList();

        var result = StutterAnalyzer.Analyze(intervals, Settings);

        Assert.Equal(0, result.StutterCount);
        Assert.Equal(4, result.MeanFrameToFrameDeltaMs, Tolerance);
    }

    [Fact]
    public void Empty_input_gives_zeros() =>
        Assert.Equal(new StutterResult(0, 0, 0, 0, 0), StutterAnalyzer.Analyze([], Settings));

    [Fact]
    public void Single_interval_has_no_neighbours_and_no_stutter() =>
        Assert.Equal(new StutterResult(0, 0, 0, 0, 0), StutterAnalyzer.Analyze([50], Settings));
}
