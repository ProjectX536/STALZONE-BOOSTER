using Booster.Core.Abstractions;
using Booster.Core.Inventory;

namespace Booster.Core.Sensors;

public sealed record CpuTelemetry(
    Reading<double> EffectiveFrequencyMhz,
    Reading<double> NominalFrequencyMhz,
    Reading<double> PerformancePercent,
    Reading<double> PerformanceLimitPercent,
    Reading<double> TemperatureC);

public sealed record GpuSensorReading(GpuAdapterInfo Adapter, string ProviderId, GpuTelemetry Telemetry);

public sealed record SensorSnapshot(DateTimeOffset TakenAt, IReadOnlyList<GpuSensorReading> Gpus, CpuTelemetry Cpu);

public sealed record SensorSurvey(SensorSnapshot Sensors, IReadOnlyList<GpuSettingsResult> Settings);
