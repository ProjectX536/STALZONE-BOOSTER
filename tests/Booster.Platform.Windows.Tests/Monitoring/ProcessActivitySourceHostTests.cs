using Booster.Core.Abstractions;
using Booster.Platform.Windows.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booster.Platform.Windows.Tests.Monitoring;

[Trait("Category", "Host")]
public class ProcessActivitySourceHostTests
{
    private static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(250);

    [Fact]
    public void Own_process_is_found_by_pid_and_has_threads()
    {
        using var source = new ProcessActivitySource(TimeProvider.System, NullLogger<ProcessActivitySource>.Instance);

        var first = source.Sample(null, Environment.ProcessId, topCount: 5);
        Thread.Sleep(Gap);
        var second = source.Sample(null, Environment.ProcessId, topCount: 5);

        Assert.Equal(DataStatus.Unknown, first.Target.Status);
        Assert.Equal(DataStatus.Available, second.Target.Status);
        var target = second.Target.Value!;
        Assert.Equal(Environment.ProcessId, target.ProcessId);
        Assert.Equal(Path.GetFileName(Environment.ProcessPath), target.Name, ignoreCase: true);
        Assert.True(target.ThreadCount > 0);
        Assert.InRange(target.CpuPercent, 0, 100);
        Assert.InRange(target.BusiestThreadPercent, 0, 100);
        Assert.Equal(DataStatus.Available, second.SystemCpuPercent.Status);
        Assert.InRange(second.SystemCpuPercent.Value, 0, 100);
        Assert.InRange(second.TopOthers.Count, 0, 5);
    }
}
