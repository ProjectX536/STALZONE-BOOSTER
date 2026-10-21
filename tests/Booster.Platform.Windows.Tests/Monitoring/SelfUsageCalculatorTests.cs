using Booster.Platform.Windows.Monitoring;

namespace Booster.Platform.Windows.Tests.Monitoring;

public class SelfUsageCalculatorTests
{
    private const double Tolerance = 1e-9;

    [Theory]
    [InlineData(1.0, 2.0, 4, 12.5)]
    [InlineData(0.0, 2.0, 4, 0.0)]
    [InlineData(8.0, 2.0, 4, 100.0)]
    [InlineData(0.5, 1.0, 1, 50.0)]
    public void Cpu_percent_is_share_of_all_processors(double cpuSeconds, double wallSeconds, int processors, double expected)
    {
        Assert.Equal(expected,
            SelfUsageCalculator.CpuPercent(TimeSpan.FromSeconds(cpuSeconds), TimeSpan.FromSeconds(wallSeconds), processors), Tolerance);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void Zero_or_negative_wall_time_gives_zero(double wallSeconds)
    {
        Assert.Equal(0, SelfUsageCalculator.CpuPercent(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(wallSeconds), 4));
    }

    [Fact]
    public void Result_is_clamped_to_0_100()
    {
        Assert.Equal(100, SelfUsageCalculator.CpuPercent(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(2), 4));
        Assert.Equal(0, SelfUsageCalculator.CpuPercent(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(2), 4));
    }

    [Fact]
    public void Zero_processors_gives_zero()
    {
        Assert.Equal(0, SelfUsageCalculator.CpuPercent(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0));
    }
}
