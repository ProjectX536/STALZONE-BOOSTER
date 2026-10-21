using Booster.Core.Abstractions;
using Booster.Platform.Windows.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booster.Platform.Windows.Tests.Monitoring;

/// <summary>Живая система, только чтение: GlobalMemoryStatusEx и счётчики производительности.</summary>
[Trait("Category", "Host")]
public class SystemPressureSourceHostTests
{
    [Fact]
    public void Sample_twice_gives_memory_values()
    {
        using var source = new SystemPressureSource(NullLogger<SystemPressureSource>.Instance);

        var first = source.Sample();
        Thread.Sleep(TimeSpan.FromMilliseconds(250));
        var second = source.Sample();

        foreach (var pressure in new[] { first, second })
        {
            Assert.Equal(DataStatus.Available, pressure.AvailableMemoryPercent.Status);
            Assert.InRange(pressure.AvailableMemoryPercent.Value, 0, 100);
            Assert.Equal(DataStatus.Available, pressure.CommitPercent.Status);
            Assert.InRange(pressure.CommitPercent.Value, 0, 100);
        }
        Assert.NotEqual(DataStatus.Available, first.PageReadsPerSecond.Status);
        Assert.NotEqual(DataStatus.Unknown, second.PageReadsPerSecond.Status);
        Assert.NotEqual(DataStatus.Unknown, second.DiskBusyPercent.Status);
    }
}
