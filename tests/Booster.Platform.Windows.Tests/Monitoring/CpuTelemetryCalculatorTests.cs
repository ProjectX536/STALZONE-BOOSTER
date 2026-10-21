using Booster.Core.Abstractions;
using Booster.Core.Resources;
using Booster.Platform.Windows.Monitoring;

namespace Booster.Platform.Windows.Tests.Monitoring;

public class CpuTelemetryCalculatorTests
{
    private const string Source = "test-counters";

    [Fact]
    public void Effective_frequency_is_nominal_times_performance()
    {
        var cpu = CpuTelemetryCalculator.Compute(performancePercent: 140, nominalMhz: 2500, limitPercent: 100, Source);

        Assert.Equal(DataStatus.Available, cpu.EffectiveFrequencyMhz.Status);
        Assert.Equal(3500, cpu.EffectiveFrequencyMhz.Value, precision: 6);
        Assert.Equal(Source, cpu.EffectiveFrequencyMhz.Source);
        Assert.Equal(2500, cpu.NominalFrequencyMhz.Value);
        Assert.Equal(140, cpu.PerformancePercent.Value);
    }

    [Fact]
    public void Missing_performance_gives_unknown_effective_frequency()
    {
        var cpu = CpuTelemetryCalculator.Compute(performancePercent: null, nominalMhz: 2500, limitPercent: 100, Source);

        Assert.Equal(DataStatus.Unknown, cpu.EffectiveFrequencyMhz.Status);
        Assert.Equal(DataStatus.Unknown, cpu.PerformancePercent.Status);
        Assert.Equal(DataStatus.Available, cpu.NominalFrequencyMhz.Status);
    }

    [Fact]
    public void Temperature_is_always_unsupported()
    {
        var cpu = CpuTelemetryCalculator.Compute(100, 2500, 100, Source);

        Assert.Equal(DataStatus.NotSupported, cpu.TemperatureC.Status);
        Assert.Equal(ReportStrings.CpuTemperatureNoSource, cpu.TemperatureC.Note);
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(88, 12)]
    [InlineData(105, 0)]
    public void Performance_limit_is_the_share_below_guaranteed_nominal(double counter, double limited)
    {
        var cpu = CpuTelemetryCalculator.Compute(100, 2500, counter, Source);

        Assert.Equal(DataStatus.Available, cpu.PerformanceLimitPercent.Status);
        Assert.Equal(limited, cpu.PerformanceLimitPercent.Value, precision: 6);
    }

    [Fact]
    public void Missing_limit_counter_gives_unknown_limit()
    {
        var cpu = CpuTelemetryCalculator.Compute(100, 2500, limitPercent: null, Source);

        Assert.Equal(DataStatus.Unknown, cpu.PerformanceLimitPercent.Status);
    }
}
