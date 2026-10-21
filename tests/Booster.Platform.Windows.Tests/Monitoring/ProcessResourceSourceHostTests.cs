using Booster.Core.Abstractions;
using Booster.Platform.Windows.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booster.Platform.Windows.Tests.Monitoring;

[Trait("Category", "Host")]
public class ProcessResourceSourceHostTests
{
    private static ProcessResourceSource Create() => new(TimeProvider.System, NullLogger<ProcessResourceSource>.Instance);

    [Fact]
    public void Memory_includes_current_process()
    {
        using var source = Create();

        var all = source.ReadMemory(int.MaxValue);

        var own = Assert.Single(all, p => p.ProcessId == Environment.ProcessId);
        Assert.True(own.PrivateBytes > 0);
        Assert.True(own.WorkingSetBytes > 0);
        Assert.DoesNotContain(all, p => p.ProcessId == 0);
    }

    [Fact]
    public void Second_io_sample_is_known()
    {
        using var source = Create();

        var first = source.SampleIo(5);
        Thread.Sleep(250);
        var second = source.SampleIo(5);

        Assert.Equal(DataStatus.Unknown, first.Status);
        Assert.Equal(DataStatus.Available, second.Status);
        Assert.InRange(second.Value!.TopIo.Count, 0, 5);
        Assert.InRange(second.Value.TopHardFaults.Count, 0, 5);
    }
}
