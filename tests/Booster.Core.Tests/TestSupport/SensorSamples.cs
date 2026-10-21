using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Inventory;
using Booster.Core.Sensors;

namespace Booster.Core.Tests.TestSupport;

/// <summary>Замеры датчиков для тестов: один адаптер по умолчанию, замеры раз в 2 с.</summary>
public static class SensorSamples
{
    public const string Source = "test-sensors";

    /// <summary>Значения по умолчанию из config/thresholds.json.</summary>
    public static readonly ThrottleSettings Defaults = new(2, 90, 10, 10, 30);

    public static readonly GpuAdapterInfo Gpu = Adapter(SnapshotFactory.GpuName, SnapshotFactory.GpuLuid);

    public static GpuAdapterInfo Adapter(string name, AdapterLuid luid) =>
        new(name, GpuVendor.Nvidia, Reading<PciId>.Unknown(), Reading<AdapterLuid>.Known(luid, Source), Reading<GpuAdapterKind>.Known(GpuAdapterKind.Discrete, Source),
            Reading<long>.Unknown(), Reading<long>.Unknown(), Reading<string>.Unknown(), Reading<DateOnly>.Unknown(), Reading<int>.Unknown(), false, null);

    /// <summary>null — значение Unknown.</summary>
    public static GpuTelemetry Telemetry(double? utilization = 95, double? temperature = 70, double? temperatureLimit = 83, double? coreClock = 1900,
        GpuThrottleReasons? reasons = GpuThrottleReasons.None) =>
        GpuTelemetry.AllUnknown(Source, null) with
        {
            UtilizationPercent = Value(utilization),
            TemperatureC = Value(temperature),
            TemperatureLimitC = Value(temperatureLimit),
            CoreClockMhz = Value(coreClock),
            ThrottleReasons = reasons is { } r ? Reading<GpuThrottleReasons>.Known(r, Source) : Reading<GpuThrottleReasons>.Unknown(Source),
        };

    public static CpuTelemetry Cpu(double? performanceLimitPercent = 0) => new(
        Reading<double>.Unknown(Source), Reading<double>.Unknown(Source), Reading<double>.Unknown(Source), Value(performanceLimitPercent),
        Reading<double>.Unknown(Source));

    /// <summary>Замер с одним адаптером <see cref="Gpu"/>.</summary>
    public static SensorSnapshot Snapshot(int index, GpuTelemetry gpu, CpuTelemetry? cpu = null) =>
        Snapshot(index, cpu, (Gpu, gpu));

    public static SensorSnapshot Snapshot(int index, CpuTelemetry? cpu, params (GpuAdapterInfo Adapter, GpuTelemetry Telemetry)[] gpus) =>
        new(SessionFactory.Start.AddSeconds(2 * index), [.. gpus.Select(g => new GpuSensorReading(g.Adapter, Source, g.Telemetry))], cpu ?? Cpu());

    /// <summary>Прогон: по замеру на каждую телеметрию адаптера <see cref="Gpu"/>.</summary>
    public static IReadOnlyList<SensorSnapshot> Run(params GpuTelemetry[] gpus) =>
        [.. gpus.Select((g, i) => Snapshot(i, g))];

    public static IReadOnlyList<SensorSnapshot> Run(int count, Func<int, GpuTelemetry> gpu) =>
        [.. Enumerable.Range(0, count).Select(i => Snapshot(i, gpu(i)))];

    private static Reading<double> Value(double? value) =>
        value is { } v ? Reading<double>.Known(v, Source) : Reading<double>.Unknown(Source);
}
