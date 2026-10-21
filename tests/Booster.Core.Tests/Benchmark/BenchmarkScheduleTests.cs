using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.Benchmark;

public class BenchmarkScheduleTests
{
    // 3 прогона по 30 с, пауза 10 с, задержка старта 10 с.
    private static readonly BenchmarkSchedule Default = BenchmarkSchedule.From(FakeConfiguration.DefaultBenchmark);

    private static FrameSample Frame(long? qpc) => new(qpc, 10, null, null, null);

    [Fact]
    public void From_settings_takes_run_count_and_seconds() =>
        Assert.Equal(new BenchmarkSchedule(3, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)), Default);

    [Fact]
    public void Capture_duration_covers_delay_runs_pauses_and_margin() =>
        Assert.Equal(TimeSpan.FromSeconds(122), Default.CaptureDuration);

    [Fact]
    public void Run_start_offsets_skip_delay_previous_runs_and_pauses()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), Default.RunStartOffset(0));
        Assert.Equal(TimeSpan.FromSeconds(50), Default.RunStartOffset(1));
        Assert.Equal(TimeSpan.FromSeconds(90), Default.RunStartOffset(2));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Run_index_outside_schedule_is_rejected(int index) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Default.RunStartOffset(index));

    [Fact]
    public void Windows_follow_offsets_in_qpc_ticks()
    {
        var windows = Default.Windows(startTimestamp: 1_000_000, timestampFrequency: 10_000_000);

        Assert.Equal(3, windows.Count);
        Assert.Equal(new RunWindow(0, 101_000_000, 401_000_000), windows[0]);
        Assert.Equal(501_000_000, windows[1].StartQpc);
        Assert.Equal(new RunWindow(2, 901_000_000, 1_201_000_000), windows[2]);
    }

    [Fact]
    public void Window_contains_its_start_but_not_its_end()
    {
        var window = new RunWindow(0, 100, 200);

        Assert.False(window.Contains(99));
        Assert.True(window.Contains(100));
        Assert.True(window.Contains(199));
        Assert.False(window.Contains(200));
    }

    [Fact]
    public void Slice_drops_first_frame_of_each_window_and_frames_outside()
    {
        FrameSample[] frames = [Frame(50), Frame(100), Frame(120), Frame(150), Frame(199), Frame(250)];

        var runs = RunSlicer.Slice(frames, [new RunWindow(0, 100, 200)]);

        Assert.Equal([Frame(120), Frame(150), Frame(199)], Assert.Single(runs));
    }

    [Fact]
    public void Frame_on_window_end_belongs_to_next_window_only()
    {
        FrameSample[] frames = [Frame(110), Frame(150), Frame(200), Frame(250), Frame(260)];

        var runs = RunSlicer.Slice(frames, [new RunWindow(0, 100, 200), new RunWindow(1, 200, 300)]);

        Assert.Equal([Frame(150)], runs[0]);
        // Кадр на границе — первый во втором окне: его интервал начался в первом, поэтому он не учтён нигде.
        Assert.Equal([Frame(250), Frame(260)], runs[1]);
    }

    [Fact]
    public void Frames_without_timestamps_are_not_sliced()
    {
        FrameSample[] frames = [Frame(100), Frame(null), Frame(150), Frame(160)];

        var runs = RunSlicer.Slice(frames, [new RunWindow(0, 100, 200)]);

        Assert.Equal([Frame(150), Frame(160)], Assert.Single(runs));
    }

    [Fact]
    public void Window_without_frames_gives_empty_run()
    {
        var runs = RunSlicer.Slice([Frame(10), Frame(20)], [new RunWindow(0, 100, 200)]);

        Assert.Empty(Assert.Single(runs));
    }
}
