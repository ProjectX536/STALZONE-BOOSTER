using Booster.Core.Sensors;

namespace Booster.Core.Abstractions;

public interface ICpuSensorSource
{
    CpuTelemetry Sample();

    IReadOnlyList<CapabilityReport> DescribeCapabilities();
}
