using Booster.Core.Abstractions;
using Booster.Core.Inventory;

namespace Booster.Platform.Windows.Hardware;

public sealed record KmtPerfData(Reading<double> TemperatureC, Reading<double> TemperatureLimitC, Reading<int> FanRpm,
    Reading<double> MemoryClockMhz);

public interface IKmtPerfDataSource
{
    KmtPerfData Read(AdapterLuid luid);
}
