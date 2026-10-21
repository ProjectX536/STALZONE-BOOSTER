using Booster.Core.Abstractions;
using Booster.Platform.Windows.Hardware;

namespace Booster.Platform.Windows.Tests.Hardware;

/// <summary>Живая система, только чтение: D3DKMTQueryAdapterInfo(KMTQAITYPE_ADAPTERPERFDATA и …_CAPS).</summary>
[Trait("Category", "Host")]
public class KmtPerfDataSourceHostTests
{
    [Fact]
    public void Reads_every_hardware_adapter_without_throwing()
    {
        var hardware = new DxgiAdapterSource().Enumerate().Where(a => !a.IsSoftware).ToList();
        var source = new KmtPerfDataSource();

        Assert.NotEmpty(hardware);
        foreach (var adapter in hardware)
        {
            var data = source.Read(adapter.Luid);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"{adapter.Description} {adapter.Luid}: temperature {Show(data.TemperatureC)}, limit {Show(data.TemperatureLimitC)}, " +
                $"fan {Show(data.FanRpm)}, memory clock {Show(data.MemoryClockMhz)}");
            if (data.TemperatureC.IsAvailable)
                Assert.InRange(data.TemperatureC.Value, 5, 120);
        }
    }

    private static string Show<T>(Reading<T> reading) where T : struct =>
        reading.IsAvailable ? $"{reading.Value}" : $"{reading.Status} ({reading.Note})";
}
