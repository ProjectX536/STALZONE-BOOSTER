using Booster.Platform.Windows.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booster.Platform.Windows.Tests.Providers;

/// <summary>Читают счётчики времени процессоров и загрузку памяти; ничего не меняют.</summary>
[Trait("Category", "Host")]
public class SystemLoadSourceHostTests
{
    [Fact]
    public async Task Cpu_needs_two_samples_and_memory_is_a_percentage()
    {
        var source = new SystemLoadSource(NullLogger<SystemLoadSource>.Instance);

        var first = source.Sample();
        await Task.Delay(200, TestContext.Current.CancellationToken);
        var second = source.Sample();

        Assert.Null(first.CpuPercent);
        Assert.NotNull(second.CpuPercent);
        Assert.InRange(second.CpuPercent.Value, 0, 100);
        Assert.NotNull(first.MemoryLoadPercent);
        Assert.True(first.MemoryLoadPercent > 0 && first.MemoryLoadPercent <= 100, $"memory load {first.MemoryLoadPercent}");
    }
}
