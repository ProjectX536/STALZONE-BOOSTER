using Booster.Core.Abstractions;
using Booster.Core.Inventory;

namespace Booster.Core.Sensors;

/// <summary>
/// Частота, производительность относительно номинала, предел производительности и температура CPU.
/// <see cref="PerformanceLimitPercent"/> — на сколько процентов номинала ограничена гарантированная производительность:
/// 0 — ограничения нет.
/// </summary>
public sealed record CpuTelemetry(
    Reading<double> EffectiveFrequencyMhz,
    Reading<double> NominalFrequencyMhz,
    Reading<double> PerformancePercent,
    Reading<double> PerformanceLimitPercent,
    Reading<double> TemperatureC);

/// <summary>Телеметрия адаптера после слияния данных провайдеров; <see cref="ProviderId"/> — вендорский, если он был.</summary>
public sealed record GpuSensorReading(GpuAdapterInfo Adapter, string ProviderId, GpuTelemetry Telemetry);

/// <summary>Один замер датчиков всех адаптеров и CPU.</summary>
public sealed record SensorSnapshot(DateTimeOffset TakenAt, IReadOnlyList<GpuSensorReading> Gpus, CpuTelemetry Cpu);

/// <summary>Замер вместе с параметрами драйвера: глобальными и для каждого приложения из запроса.</summary>
public sealed record SensorSurvey(SensorSnapshot Sensors, IReadOnlyList<GpuSettingsResult> Settings);
