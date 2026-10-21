using Booster.Core.Abstractions;
using Booster.Core.Benchmark;

namespace Booster.Core.Tests.Benchmark;

public class FrameStatsTests
{
    private const double Tolerance = 1e-9;

    private static FrameSample Frame(double intervalMs, double? cpu = null, double? gpu = null, int? sync = null) =>
        new(null, intervalMs, cpu, gpu, sync);

    [Fact]
    public void GpuBusyShare_is_median_of_busy_over_interval()
    {
        var stats = FrameStatsCalculator.Compute([Frame(10, gpu: 9), Frame(10, gpu: 9), Frame(10, gpu: 5)]);

        Assert.Equal(3, stats.Frames);
        Assert.Equal(0.9, stats.GpuBusyShare!.Value, Tolerance);
    }

    [Fact]
    public void Median_of_even_count_is_mean_of_two_middle_values()
    {
        var stats = FrameStatsCalculator.Compute([Frame(10, cpu: 2), Frame(10, cpu: 8), Frame(10, cpu: 4), Frame(10, cpu: 6)]);

        Assert.Equal(0.5, stats.CpuBusyShare!.Value, Tolerance);
    }

    [Fact]
    public void Busy_longer_than_interval_counts_as_full_share()
    {
        // MsCPUBusy включает ожидание ограничителя и может превышать интервал.
        var stats = FrameStatsCalculator.Compute([Frame(10, cpu: 25), Frame(10, cpu: 12)]);

        Assert.Equal(1.0, stats.CpuBusyShare!.Value, Tolerance);
    }

    [Fact]
    public void Frames_without_busy_value_are_left_out_of_the_share()
    {
        var stats = FrameStatsCalculator.Compute([Frame(10, gpu: 4), Frame(10), Frame(10)]);

        Assert.Equal(0.4, stats.GpuBusyShare!.Value, Tolerance);
    }

    [Fact]
    public void SyncedShare_counts_SyncInterval_at_least_one()
    {
        var stats = FrameStatsCalculator.Compute(
            [Frame(16.7, sync: 1), Frame(16.7, sync: 2), Frame(16.7, sync: 0), Frame(16.7, sync: 1), Frame(16.7)]);

        Assert.Equal(0.75, stats.SyncedShare!.Value, Tolerance);
        Assert.Equal(1, stats.TypicalSyncInterval);
    }

    [Fact]
    public void Typical_sync_interval_is_the_most_frequent_value()
    {
        var stats = FrameStatsCalculator.Compute([Frame(33.3, sync: 2), Frame(33.3, sync: 2), Frame(16.7, sync: 1)]);

        Assert.Equal(2, stats.TypicalSyncInterval);
    }

    [Fact]
    public void Missing_columns_give_null_shares() =>
        Assert.Equal(new FrameStats(2, null, null, null, null), FrameStatsCalculator.Compute([Frame(10), Frame(11)]));

    [Fact]
    public void No_frames_give_zero_count_and_null_shares() =>
        Assert.Equal(new FrameStats(0, null, null, null, null), FrameStatsCalculator.Compute([]));
}
