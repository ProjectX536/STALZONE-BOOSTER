using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Tests.Common;

public sealed class BootTimeSourceTests
{
    [Fact]
    public void Compute_subtracts_uptime()
    {
        var now = new DateTimeOffset(2026, 10, 21, 15, 30, 0, TimeSpan.FromHours(3));

        var boot = BootTimeSource.Compute(now, (long)TimeSpan.FromHours(26.5).TotalMilliseconds);

        Assert.Equal(new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero), boot);
        Assert.Equal(TimeSpan.Zero, boot.Offset);
    }

    [Fact]
    [Trait("Category", "Host")]
    public void Real_boot_time_is_in_the_past()
    {
        var boot = new BootTimeSource(TimeProvider.System).LastBootTime();

        Assert.True(boot.IsAvailable);
        Assert.True(boot.Value < DateTimeOffset.UtcNow);
    }
}
