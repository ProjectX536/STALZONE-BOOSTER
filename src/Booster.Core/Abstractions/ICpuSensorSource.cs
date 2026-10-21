using Booster.Core.Sensors;

namespace Booster.Core.Abstractions;

/// <summary>Датчики CPU: частота, производительность и её предел, температура. Только чтение.</summary>
public interface ICpuSensorSource
{
    /// <summary>Относительные счётчики требуют двух замеров: первый вызов возвращает их Unknown.</summary>
    CpuTelemetry Sample();

    IReadOnlyList<CapabilityReport> DescribeCapabilities();
}
