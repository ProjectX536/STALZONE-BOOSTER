using Booster.App.Tests.TestSupport;
using Booster.App.ViewModels;
using Booster.Core.Abstractions;
using Booster.Core.Reports;
using Booster.Core.Resources;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booster.App.Tests.ViewModels;

public sealed class HardwareViewModelTests : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(2000);

    private readonly ScreenSession _screen = new();
    private readonly FakeSensorProvider _sensors;
    private readonly HardwareViewModel _hardware;

    public HardwareViewModelTests()
    {
        _sensors = new FakeSensorProvider(_screen.Time);
        _hardware = ScreenSession.OnUiContext(() => new HardwareViewModel(_screen.Report, _sensors, _screen.Config, _screen.Time,
            NullLogger<HardwareViewModel>.Instance));
    }

    [Fact]
    public async Task Activate_reads_sensors_every_interval()
    {
        _hardware.Activate();
        await _sensors.WaitForCallsAsync(1);
        _screen.Time.Advance(Interval);
        await _sensors.WaitForCallsAsync(2);
        _screen.Time.Advance(Interval);
        await _sensors.WaitForCallsAsync(3);

        Assert.Equal(3, _sensors.Calls);
    }

    [Fact]
    public async Task Deactivate_stops_reading()
    {
        _hardware.Activate();
        await _sensors.WaitForCallsAsync(1);

        _hardware.Deactivate();
        _screen.Time.Advance(Interval);
        _screen.Time.Advance(Interval);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(1, _sensors.Calls);
    }

    [Fact]
    public async Task Double_activate_does_not_start_a_second_loop()
    {
        _hardware.Activate();
        await _sensors.WaitForCallsAsync(1);
        _hardware.Activate();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(1, _sensors.Calls);

        _screen.Time.Advance(Interval);
        await _sensors.WaitForCallsAsync(2);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(2, _sensors.Calls);
    }

    [Fact]
    public async Task Activate_does_not_block_on_a_hanging_sensor_read()
    {
        _sensors.BlockOnCall = 1;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        _hardware.Activate();
        watch.Stop();
        await _sensors.WaitForCallsAsync(1);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"Activate blocked for {watch.Elapsed}");
    }

    [Fact]
    public async Task Unknown_reading_shows_unknown_text()
    {
        _sensors.Sample = at => SensorSamples.Snapshot(0, SensorSamples.Telemetry(utilization: null));

        _hardware.Activate();
        await ScreenSession.WaitUntilAsync(() => _hardware.Sensors.Count > 0, "sensor rows did not appear");

        var row = Assert.Single(_hardware.Sensors, r => r.Device == SensorSamples.Gpu.Name && r.Metric == ReportStrings.SensorUtilization);
        Assert.Equal(ReadingFormatter.StatusText(DataStatus.Unknown), row.Value);
        Assert.Equal(SensorSamples.Source, row.Source);
        Assert.Contains(_hardware.Sensors, r => r.Metric == ReportStrings.CpuPerformanceLimit);
    }

    public void Dispose()
    {
        _hardware.Deactivate();
        _screen.Dispose();
    }
}
