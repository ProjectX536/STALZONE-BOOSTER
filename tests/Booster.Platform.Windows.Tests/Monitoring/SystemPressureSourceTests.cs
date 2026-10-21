using Booster.Core.Abstractions;
using Booster.Platform.Windows.Monitoring;

namespace Booster.Platform.Windows.Tests.Monitoring;

public class SystemPressureSourceTests
{
    private const double Tolerance = 1e-9;
    private const string Source = "test counter";

    private static readonly GlobalMemoryStatus Memory = new(16_000_000_000, 1_600_000_000, 32_000_000_000, 8_000_000_000);

    private static Reading<double> Known(double value) => Reading<double>.Known(value, Source);

    [Fact]
    public void Memory_percents_from_status()
    {
        var pressure = SystemPressureSource.Compute(Memory, Known(0), Known(50), Known(0.002), Known(0), Known(0));

        Assert.Equal(DataStatus.Available, pressure.AvailableMemoryPercent.Status);
        Assert.Equal(10, pressure.AvailableMemoryPercent.Value, Tolerance);
        Assert.Equal(DataStatus.Available, pressure.CommitPercent.Status);
        Assert.Equal(75, pressure.CommitPercent.Value, Tolerance);
    }

    [Fact]
    public void Missing_memory_status_fails_both_memory_values()
    {
        var pressure = SystemPressureSource.Compute(null, Known(0), Known(50), Known(0.002), Known(0), Known(0));

        Assert.Equal(DataStatus.Failed, pressure.AvailableMemoryPercent.Status);
        Assert.Equal(DataStatus.Failed, pressure.CommitPercent.Status);
    }

    [Fact]
    public void Disk_busy_is_inverse_of_idle_and_clamped()
    {
        static double Busy(double idle) => SystemPressureSource.Compute(Memory, Known(0), Known(idle), Known(0), Known(0), Known(0)).DiskBusyPercent.Value;

        Assert.Equal(0, Busy(105), Tolerance);
        Assert.Equal(70, Busy(30), Tolerance);
        Assert.Equal(100, Busy(-5), Tolerance);
    }

    [Fact]
    public void Disk_latency_is_seconds_per_transfer_in_milliseconds()
    {
        var pressure = SystemPressureSource.Compute(Memory, Known(120), Known(50), Known(0.0125), Known(0), Known(0));

        Assert.Equal(12.5, pressure.DiskLatencyMs.Value, Tolerance);
        Assert.Equal(120, pressure.PageReadsPerSecond.Value, Tolerance);
    }

    [Fact]
    public void Dpc_and_interrupt_are_clamped_to_percent()
    {
        var low = SystemPressureSource.Compute(Memory, Known(0), Known(50), Known(0), Known(-1), Known(-1));
        var high = SystemPressureSource.Compute(Memory, Known(0), Known(50), Known(0), Known(140), Known(140));
        var normal = SystemPressureSource.Compute(Memory, Known(0), Known(50), Known(0), Known(0.4), Known(1.5));

        Assert.Equal(0, low.DpcTimePercent.Value, Tolerance);
        Assert.Equal(0, low.InterruptTimePercent.Value, Tolerance);
        Assert.Equal(100, high.DpcTimePercent.Value, Tolerance);
        Assert.Equal(100, high.InterruptTimePercent.Value, Tolerance);
        Assert.Equal(0.4, normal.DpcTimePercent.Value, Tolerance);
        Assert.Equal(1.5, normal.InterruptTimePercent.Value, Tolerance);
    }

    [Fact]
    public void Unknown_latency_counters_pass_through()
    {
        var missing = SystemPressureSource.CounterReading(SystemPressureSource.DpcTimeSource, openError: "Category does not exist.",
            justOpened: true, value: null);
        var unread = SystemPressureSource.CounterReading(SystemPressureSource.InterruptTimeSource, openError: null, justOpened: false,
            value: null);

        var pressure = SystemPressureSource.Compute(Memory, Known(0), Known(50), Known(0), missing, unread);

        Assert.Equal(DataStatus.NotSupported, pressure.DpcTimePercent.Status);
        Assert.Equal(SystemPressureSource.DpcTimeSource, pressure.DpcTimePercent.Source);
        Assert.Equal(DataStatus.Failed, pressure.InterruptTimePercent.Status);
        Assert.Equal(SystemPressureSource.InterruptTimeSource, pressure.InterruptTimePercent.Source);
    }

    [Fact]
    public void Rate_counters_first_sample_is_unknown()
    {
        var first = SystemPressureSource.CounterReading(Source, openError: null, justOpened: true, value: 0);
        var pressure = SystemPressureSource.Compute(Memory, first, first, first, first, first);

        Assert.Equal(DataStatus.Unknown, first.Status);
        Assert.Equal(SystemPressureSource.NeedsSecondReadingNote, first.Note);
        Assert.Equal(DataStatus.Unknown, pressure.PageReadsPerSecond.Status);
        Assert.Equal(DataStatus.Unknown, pressure.DiskBusyPercent.Status);
        Assert.Equal(DataStatus.Unknown, pressure.DiskLatencyMs.Status);
        Assert.Equal(DataStatus.Unknown, pressure.DpcTimePercent.Status);
        Assert.Equal(DataStatus.Unknown, pressure.InterruptTimePercent.Status);
        Assert.Equal(DataStatus.Available, pressure.AvailableMemoryPercent.Status);

        var second = SystemPressureSource.CounterReading(Source, openError: null, justOpened: false, value: 42);
        Assert.Equal(DataStatus.Available, second.Status);
        Assert.Equal(42, second.Value, Tolerance);
    }

    [Fact]
    public void Counter_that_did_not_open_is_not_supported_and_unread_value_is_failed()
    {
        var missing = SystemPressureSource.CounterReading(Source, openError: "Category does not exist.", justOpened: true, value: null);
        var unread = SystemPressureSource.CounterReading(Source, openError: null, justOpened: false, value: null);

        Assert.Equal(DataStatus.NotSupported, missing.Status);
        Assert.Equal("Category does not exist.", missing.Note);
        Assert.Equal(DataStatus.Failed, unread.Status);
        Assert.Equal(DataStatus.NotSupported, SystemPressureSource.Compute(Memory, missing, missing, missing, missing, missing).DiskBusyPercent.Status);
    }
}
