using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Core.Sensors;
using Booster.Core.Tests.TestSupport;
using static Booster.Core.Tests.TestSupport.SensorSamples;

namespace Booster.Core.Tests.Sensors;

public sealed class ThermalThrottleDetectorTests
{
    [Fact]
    public void Driver_thermal_reason_is_high_confidence()
    {
        var samples = Run(Telemetry(), Telemetry(reasons: GpuThrottleReasons.Thermal), Telemetry());

        var assessment = ThermalThrottleDetector.Assess(samples, Defaults);

        var finding = Assert.Single(assessment.Findings);
        Assert.Equal((ThrottleSignal.GpuDriverThermal, ThrottleConfidence.High), (finding.Signal, finding.Confidence));
        Assert.Equal((1, 3), (finding.Samples, finding.TotalSamples));
        Assert.Equal(Gpu.Name, finding.AdapterName);
        Assert.False(assessment.EnoughSamples);
    }

    [Fact]
    public void Driver_power_and_other_reasons_have_their_own_signals()
    {
        var samples = Run(Telemetry(reasons: GpuThrottleReasons.PowerLimit), Telemetry(reasons: GpuThrottleReasons.PowerSource | GpuThrottleReasons.ApiTriggered));

        var assessment = ThermalThrottleDetector.Assess(samples, Defaults);

        Assert.Equal([ThrottleSignal.GpuDriverPowerLimit, ThrottleSignal.GpuDriverOther], assessment.Findings.Select(f => f.Signal));
        Assert.All(assessment.Findings, f => Assert.Equal(ThrottleConfidence.High, f.Confidence));
    }

    [Fact]
    public void Temperature_within_margin_of_the_driver_limit_is_medium()
    {
        var atLimit = ThermalThrottleDetector.Assess(Run(Telemetry(temperature: 75), Telemetry(temperature: 81)), Defaults);
        var belowLimit = ThermalThrottleDetector.Assess(Run(Telemetry(temperature: 75), Telemetry(temperature: 80)), Defaults);

        var finding = Assert.Single(atLimit.Findings);
        Assert.Equal((ThrottleSignal.GpuTemperatureAtLimit, ThrottleConfidence.Medium), (finding.Signal, finding.Confidence));
        Assert.Equal(81, finding.Peak.Value);
        Assert.Equal((1, 2), (finding.Samples, finding.TotalSamples));
        Assert.Empty(belowLimit.Findings);
    }

    [Fact]
    public void Unknown_limit_gives_no_temperature_finding()
    {
        var assessment = ThermalThrottleDetector.Assess(Run(Telemetry(temperature: 95, temperatureLimit: null)), Defaults);

        Assert.Empty(assessment.Findings);
    }

    [Fact]
    public void Clock_drop_under_load_without_a_reason_is_low()
    {
        var samples = Run(10, i => Telemetry(utilization: 95, coreClock: i < 8 ? 1900 : 1650));

        var assessment = ThermalThrottleDetector.Assess(samples, Defaults);

        var finding = Assert.Single(assessment.Findings);
        Assert.Equal((ThrottleSignal.GpuClockDropUnderLoad, ThrottleConfidence.Low), (finding.Signal, finding.Confidence));
        Assert.Equal(1650, finding.Peak.Value);
        Assert.Equal((2, 10), (finding.Samples, finding.TotalSamples));
        Assert.True(assessment.EnoughSamples);
    }

    [Fact]
    public void Clock_drop_with_temperature_at_limit_is_medium()
    {
        var samples = Run(10, i => i < 8 ? Telemetry(coreClock: 1900) : Telemetry(coreClock: 1650, temperature: 82));

        var assessment = ThermalThrottleDetector.Assess(samples, Defaults);

        var drop = Assert.Single(assessment.Findings, f => f.Signal == ThrottleSignal.GpuClockDropUnderLoad);
        Assert.Equal(ThrottleConfidence.Medium, drop.Confidence);
        Assert.Contains(assessment.Findings, f => f.Signal == ThrottleSignal.GpuTemperatureAtLimit);
    }

    [Fact]
    public void Clock_drop_with_a_driver_power_reason_is_medium()
    {
        var samples = Run(10, i => i < 8 ? Telemetry(coreClock: 1900) : Telemetry(coreClock: 1650, reasons: GpuThrottleReasons.PowerLimit));

        var assessment = ThermalThrottleDetector.Assess(samples, Defaults);

        Assert.Equal(ThrottleConfidence.Medium, Assert.Single(assessment.Findings, f => f.Signal == ThrottleSignal.GpuClockDropUnderLoad).Confidence);
    }

    [Fact]
    public void Clock_drop_is_not_judged_on_too_few_samples()
    {
        var samples = Run(5, i => Telemetry(coreClock: i < 3 ? 1900 : 1650));

        var assessment = ThermalThrottleDetector.Assess(samples, Defaults);

        Assert.Empty(assessment.Findings);
        Assert.False(assessment.EnoughSamples);
        Assert.Equal(5, assessment.SampleCount);
    }

    [Fact]
    public void Idle_clock_changes_are_not_throttling()
    {
        var samples = Run(10, i => Telemetry(utilization: 20, coreClock: 300 + (i * 160)));

        var assessment = ThermalThrottleDetector.Assess(samples, Defaults);

        Assert.Empty(assessment.Findings);
    }

    [Fact]
    public void Cpu_performance_limit_is_low_confidence()
    {
        IReadOnlyList<SensorSnapshot> samples =
            [Snapshot(0, Telemetry(), Cpu(0)), Snapshot(1, Telemetry(), Cpu(12)), Snapshot(2, Telemetry(), Cpu(0))];

        var assessment = ThermalThrottleDetector.Assess(samples, Defaults);

        var finding = Assert.Single(assessment.Findings);
        Assert.Equal((ThrottleSignal.CpuPerformanceLimited, ThrottleConfidence.Low), (finding.Signal, finding.Confidence));
        Assert.Equal(12, finding.Peak.Value);
        Assert.Null(finding.AdapterName);
        Assert.Equal((1, 3), (finding.Samples, finding.TotalSamples));
    }

    [Fact]
    public void Healthy_run_has_no_findings()
    {
        var assessment = ThermalThrottleDetector.Assess(Run(10, _ => Telemetry()), Defaults);

        Assert.Empty(assessment.Findings);
        Assert.True(assessment.EnoughSamples);
    }

    [Fact]
    public void Adapters_are_matched_by_luid()
    {
        var first = Adapter("Intel(R) UHD Graphics", new AdapterLuid(0x0000A001, 0));
        var second = Adapter("NVIDIA GeForce RTX 3060 Ti", new AdapterLuid(0x0000C2A1, 0));
        // Тот же адаптер под другим именем в одном из замеров — всё равно тот же LUID.
        var renamed = second with { Name = "NVIDIA GeForce RTX 3060 Ti (renamed)" };
        IReadOnlyList<SensorSnapshot> samples =
        [
            Snapshot(0, null, (first, Telemetry()), (second, Telemetry())),
            Snapshot(1, null, (first, Telemetry()), (renamed, Telemetry(reasons: GpuThrottleReasons.Thermal))),
        ];

        var assessment = ThermalThrottleDetector.Assess(samples, Defaults);

        var finding = Assert.Single(assessment.Findings);
        Assert.Equal(second.Name, finding.AdapterName);
        Assert.Equal((1, 2), (finding.Samples, finding.TotalSamples));
    }

    [Fact]
    public void Empty_run_has_nothing_to_judge()
    {
        var assessment = ThermalThrottleDetector.Assess([], Defaults);

        Assert.Equal((0, false), (assessment.SampleCount, assessment.EnoughSamples));
        Assert.Empty(assessment.Findings);
    }
}
