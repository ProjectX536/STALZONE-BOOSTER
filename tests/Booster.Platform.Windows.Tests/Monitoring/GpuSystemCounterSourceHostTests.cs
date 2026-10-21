using System.Diagnostics;
using Booster.Core.Abstractions;
using Booster.Platform.Windows.Hardware;
using Booster.Platform.Windows.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booster.Platform.Windows.Tests.Monitoring;

/// <summary>Живая система, только чтение счётчиков «GPU Engine» и «GPU Adapter Memory».</summary>
[Trait("Category", "Host")]
public class GpuSystemCounterSourceHostTests
{
    [Fact]
    public void Second_sample_covers_every_hardware_adapter()
    {
        var hardware = new DxgiAdapterSource().Enumerate().Where(a => !a.IsSoftware).Select(a => a.Luid).ToList();
        var source = new GpuSystemCounterSource(NullLogger<GpuSystemCounterSource>.Instance);

        source.Sample();
        Thread.Sleep(TimeSpan.FromMilliseconds(250));
        var watch = Stopwatch.StartNew();
        var samples = source.Sample();
        TestContext.Current.TestOutputHelper?.WriteLine($"Sample(): {watch.Elapsed.TotalMilliseconds:F1} ms");

        Assert.Equal(DataStatus.Available, source.IsAvailable.Status);
        Assert.NotEmpty(hardware);
        Assert.All(hardware, luid =>
        {
            Assert.True(samples.TryGetValue(luid, out var sample), $"{luid}");
            Assert.Equal(DataStatus.Available, sample.UtilizationPercent.Status);
            Assert.InRange(sample.UtilizationPercent.Value, 0, 100);
        });
    }
}
