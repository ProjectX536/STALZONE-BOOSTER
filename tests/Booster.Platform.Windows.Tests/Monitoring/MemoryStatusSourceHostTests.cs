using Booster.Platform.Windows.Monitoring;

namespace Booster.Platform.Windows.Tests.Monitoring;

[Trait("Category", "Host")]
public class MemoryStatusSourceHostTests
{
    [Fact]
    public void Read_returns_consistent_values()
    {
        var status = new MemoryStatusSource().Read();

        Assert.True(status.PhysicalTotalBytes > status.PhysicalAvailableBytes);
        Assert.True(status.PhysicalAvailableBytes > 0);
        Assert.True(status.CommitLimitBytes >= status.CommitTotalBytes);
        Assert.True(status.ProcessCount > 0);
    }
}
