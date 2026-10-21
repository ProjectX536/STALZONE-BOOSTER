using Booster.Core.Abstractions;

namespace Booster.Core.Sensors;

public enum ThrottleSignal
{
    GpuDriverThermal,

    GpuDriverPowerLimit,

    GpuDriverOther,

    GpuTemperatureAtLimit,

    GpuClockDropUnderLoad,

    CpuPerformanceLimited,
}

public enum ThrottleConfidence
{
    Low,
    Medium,
    High,
}

public sealed record ThrottleFinding(
    ThrottleSignal Signal,
    ThrottleConfidence Confidence,
    string? AdapterName,
    int Samples,
    int TotalSamples,
    Reading<double> Peak);

public sealed record ThrottleAssessment(int SampleCount, int RequiredSamples, bool EnoughSamples, IReadOnlyList<ThrottleFinding> Findings);
