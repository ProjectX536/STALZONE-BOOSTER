using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Platform.Windows.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booster.Platform.Windows.Tests.Monitoring;

/// <summary>Живая система, только чтение счётчиков «Processor Information(_Total)».</summary>
[Trait("Category", "Host")]
public class CpuSensorSourceHostTests
{
    // Относительному счётчику нужен промежуток между чтениями.
    private static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(250);

    [Fact]
    public void Second_sample_has_effective_frequency()
    {
        using var source = new CpuSensorSource(NullLogger<CpuSensorSource>.Instance);

        source.Sample();
        Thread.Sleep(Gap);
        var cpu = source.Sample();

        Assert.Equal(DataStatus.Available, cpu.EffectiveFrequencyMhz.Status);
        Assert.InRange(cpu.EffectiveFrequencyMhz.Value, 400, 8000);
        Assert.Equal(DataStatus.Available, cpu.PerformanceLimitPercent.Status);
        Assert.InRange(cpu.PerformanceLimitPercent.Value, 0, 100);
    }

    [Fact]
    public void Frequency_and_limit_are_supported_and_temperature_is_not()
    {
        using var source = new CpuSensorSource(NullLogger<CpuSensorSource>.Instance);

        var capabilities = source.DescribeCapabilities().ToDictionary(c => c.CapabilityId, c => c.Status);

        Assert.Equal(CapabilityStatus.Supported, capabilities[CapabilityIds.CpuFrequency]);
        Assert.Equal(CapabilityStatus.Supported, capabilities[CapabilityIds.CpuPerformanceLimit]);
        Assert.Equal(CapabilityStatus.NotSupported, capabilities[CapabilityIds.CpuTemperature]);
    }
}
