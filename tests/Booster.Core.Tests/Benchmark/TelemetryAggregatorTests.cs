using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Inventory;
using Booster.Core.Sensors;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.Benchmark;

public class TelemetryAggregatorTests
{
    private const double Tolerance = 1e-9;
    private const string Source = "test";
    private const int BackgroundCount = 5;

    private static readonly RunWindow[] Windows = [new(0, 0, 100), new(1, 200, 300)];

    private static readonly GpuAdapterInfo Discrete = SensorSamples.Adapter("GeForce RTX 4070", new AdapterLuid(0x1000, 0)) with
    {
        DriverVersion = Reading<string>.Known("32.0.15.6094", Source),
    };

    private static readonly GpuAdapterInfo Integrated = SensorSamples.Adapter("Intel UHD Graphics 770", new AdapterLuid(0x2000, 0));

    private static readonly GpuAdapterInfo BasicRender = SensorSamples.Adapter("Microsoft Basic Render Driver", new AdapterLuid(0x3000, 0)) with
    {
        IsSoftware = true,
    };

    private static Reading<double> Known(double value) => Reading<double>.Known(value, Source);

    private static GpuTelemetry Gpu(double utilization, double temperature = 70, long vramUsed = 6_000, long vramTotal = 12_000) =>
        SensorSamples.Telemetry(utilization, temperature) with
        {
            VramUsedBytes = Reading<long>.Known(vramUsed, Source),
            VramTotalBytes = Reading<long>.Known(vramTotal, Source),
        };

    private static ProcessActivity Activity(double system, double target, double busiestThread, params (string Name, double Cpu)[] others) =>
        new(Known(system), Reading<TargetCpuUsage>.Known(new TargetCpuUsage("game.exe", 10, target, busiestThread, 40), Source),
            [.. others.Select((o, i) => new ProcessCpuUsage(o.Name, 100 + i, o.Cpu))]);

    private static BenchmarkSample Sample(long timestamp, SensorSnapshot? sensors = null, ProcessActivity? activity = null,
        SystemPressure? pressure = null) =>
        new(SessionFactory.Start.AddSeconds(timestamp), timestamp, sensors, activity, pressure);

    private static SensorSnapshot Sensors(params (GpuAdapterInfo Adapter, GpuTelemetry Telemetry)[] gpus) =>
        SensorSamples.Snapshot(0, null, gpus);

    private static SeriesTelemetry Aggregate(IReadOnlyList<BenchmarkSample> samples, Reading<AdapterLuid>? luid = null) =>
        TelemetryAggregator.Aggregate(samples, Windows, luid ?? Reading<AdapterLuid>.Known(Discrete.Luid.Value, Source),
            FakeConfiguration.DefaultThresholds, BackgroundCount);

    [Fact]
    public void Samples_in_pauses_are_ignored()
    {
        var samples = new[]
        {
            Sample(10, Sensors((Discrete, Gpu(90))), Activity(40, 20, 60), FakeSystemPressureSource.Pressure(30, 50, 10, 20, 2)),
            Sample(150, Sensors((Discrete, Gpu(10))), Activity(99, 99, 99), FakeSystemPressureSource.Pressure(1, 99, 999, 99, 99)),
            Sample(250, Sensors((Discrete, Gpu(96))), Activity(60, 30, 80), FakeSystemPressureSource.Pressure(20, 70, 30, 40, 4)),
            Sample(300, Sensors((Discrete, Gpu(10))), Activity(99, 99, 99), FakeSystemPressureSource.Pressure(1, 99, 999, 99, 99)),
        };

        var telemetry = Aggregate(samples);

        Assert.Equal(2, telemetry.SampleCount);
        Assert.Equal(93, telemetry.GpuUtilizationPercent.Value, Tolerance);
        Assert.Equal(50, telemetry.SystemCpuPercent.Value, Tolerance);
        Assert.Equal(25, telemetry.TargetCpuPercent.Value, Tolerance);
        Assert.Equal(70, telemetry.BusiestThreadPercent.Value, Tolerance);
        Assert.Equal(20, telemetry.AvailableMemoryMinPercent.Value, Tolerance);
        Assert.Equal(70, telemetry.CommitPeakPercent.Value, Tolerance);
        Assert.Equal(20, telemetry.PageReadsPerSecond.Value, Tolerance);
        Assert.Equal(30, telemetry.DiskBusyPercent.Value, Tolerance);
        Assert.Equal(3, telemetry.DiskLatencyMs.Value, Tolerance);
        Assert.Equal(2, telemetry.Throttle!.SampleCount);
    }

    [Fact]
    public void Latency_median_and_peak_from_samples()
    {
        var samples = new[]
        {
            Sample(10, pressure: FakeSystemPressureSource.Pressure(30, 50, 10, 20, 2, dpcTime: 0.2, interruptTime: 0.1)),
            Sample(20, pressure: FakeSystemPressureSource.Pressure(30, 50, 10, 20, 2, dpcTime: 0.4, interruptTime: 0.3)),
            Sample(150, pressure: FakeSystemPressureSource.Pressure(30, 50, 10, 20, 2, dpcTime: 99, interruptTime: 99)),
            Sample(250, pressure: FakeSystemPressureSource.Pressure(30, 50, 10, 20, 2, dpcTime: 3.0, interruptTime: 2.5)),
        };

        var telemetry = Aggregate(samples);

        Assert.Equal(0.4, telemetry.DpcTimeMedianPercent.Value, Tolerance);
        Assert.Equal(3.0, telemetry.DpcTimePeakPercent.Value, Tolerance);
        Assert.Equal(0.3, telemetry.InterruptTimeMedianPercent.Value, Tolerance);
        Assert.Equal(2.5, telemetry.InterruptTimePeakPercent.Value, Tolerance);
    }

    [Fact]
    public void Render_adapter_by_luid_gives_utilization_median_and_driver()
    {
        var samples = new[]
        {
            Sample(10, Sensors((Integrated, Gpu(5)), (Discrete, Gpu(90, temperature: 71, vramUsed: 3_000)))),
            Sample(20, Sensors((Integrated, Gpu(7)), (Discrete, Gpu(99, temperature: 78, vramUsed: 9_000)))),
            Sample(30, Sensors((Integrated, Gpu(6)), (Discrete, Gpu(95, temperature: 75, vramUsed: 6_000)))),
        };

        var telemetry = Aggregate(samples);

        Assert.Equal("GeForce RTX 4070", telemetry.RenderAdapterName.Value);
        Assert.Null(telemetry.RenderAdapterName.Note);
        Assert.Equal("32.0.15.6094", telemetry.DriverVersion.Value);
        Assert.Equal(95, telemetry.GpuUtilizationPercent.Value, Tolerance);
        Assert.Equal(78, telemetry.GpuTemperaturePeakC.Value, Tolerance);
        Assert.Equal(50, telemetry.VramUsedMedianPercent.Value, Tolerance);
        Assert.Equal(75, telemetry.VramUsedPeakPercent.Value, Tolerance);
    }

    [Fact]
    public void Single_hardware_adapter_used_when_luid_unknown_with_note()
    {
        var samples = new[]
        {
            Sample(10, Sensors((BasicRender, Gpu(0)), (Discrete, Gpu(80)))),
            Sample(20, Sensors((BasicRender, Gpu(0)), (Discrete, Gpu(90)))),
        };

        var telemetry = Aggregate(samples, Reading<AdapterLuid>.Unknown(Source));

        Assert.Equal(DataStatus.Available, telemetry.RenderAdapterName.Status);
        Assert.Equal("GeForce RTX 4070", telemetry.RenderAdapterName.Value);
        Assert.Equal(TelemetryAggregator.SingleAdapterNote, telemetry.RenderAdapterName.Note);
        Assert.Equal(85, telemetry.GpuUtilizationPercent.Value, Tolerance);
    }

    [Fact]
    public void Several_hardware_adapters_without_luid_give_unknown_adapter()
    {
        var samples = new[] { Sample(10, Sensors((Integrated, Gpu(5)), (Discrete, Gpu(90)))) };

        var telemetry = Aggregate(samples, Reading<AdapterLuid>.Unknown(Source));

        Assert.Equal(DataStatus.Unknown, telemetry.RenderAdapterName.Status);
        Assert.Equal(DataStatus.Unknown, telemetry.GpuUtilizationPercent.Status);
        Assert.Equal(DataStatus.Unknown, telemetry.DriverVersion.Status);
    }

    [Fact]
    public void No_samples_gives_unknown_readings_and_null_throttle()
    {
        var telemetry = Aggregate([Sample(150, Sensors((Discrete, Gpu(90))), Activity(40, 20, 60))]);

        Assert.Equal(0, telemetry.SampleCount);
        Assert.Null(telemetry.Throttle);
        Assert.Empty(telemetry.Background);
        Assert.Equal(DataStatus.Unknown, telemetry.GpuUtilizationPercent.Status);
        Assert.Equal(DataStatus.Unknown, telemetry.SystemCpuPercent.Status);
        Assert.Equal(DataStatus.Unknown, telemetry.BusiestThreadPercent.Status);
        Assert.Equal(DataStatus.Unknown, telemetry.AvailableMemoryMinPercent.Status);
        Assert.Equal(DataStatus.Unknown, telemetry.DiskLatencyMs.Status);
    }

    [Fact]
    public void Status_of_unavailable_readings_is_kept()
    {
        var pressure = FakeSystemPressureSource.Pressure(30, 50, 10, 20, 2) with
        {
            DiskBusyPercent = Reading<double>.Unsupported(Source, "no PhysicalDisk category"),
        };

        var telemetry = Aggregate([Sample(10, pressure: pressure), Sample(20, pressure: pressure)]);

        Assert.Equal(DataStatus.NotSupported, telemetry.DiskBusyPercent.Status);
        Assert.Equal("no PhysicalDisk category", telemetry.DiskBusyPercent.Note);
        Assert.Equal(DataStatus.Available, telemetry.DiskLatencyMs.Status);
    }

    [Fact]
    public void Background_median_counts_absence_as_zero()
    {
        var samples = new[]
        {
            Sample(10, activity: Activity(50, 20, 60, ("chrome.exe", 10), ("steam.exe", 30), ("discord.exe", 4))),
            Sample(20, activity: Activity(50, 20, 60, ("chrome.exe", 10), ("discord.exe", 4))),
            Sample(30, activity: Activity(50, 20, 60, ("chrome.exe", 6), ("chrome.exe", 4))),
        };

        var telemetry = Aggregate(samples);

        Assert.Equal([new BackgroundProcess("chrome.exe", 10), new BackgroundProcess("discord.exe", 4)], telemetry.Background);
    }

    [Fact]
    public void Background_is_limited_to_requested_count()
    {
        var others = Enumerable.Range(1, 8).Select(i => ($"p{i}.exe", (double)i)).ToArray();
        var samples = new[] { Sample(10, activity: Activity(50, 20, 60, others)) };

        var telemetry = Aggregate(samples);

        Assert.Equal(["p8.exe", "p7.exe", "p6.exe", "p5.exe", "p4.exe"], telemetry.Background.Select(b => b.Name));
    }
}
