using Booster.Core.Abstractions;
using Booster.Core.Inventory;

namespace Booster.Platform.Windows.Hardware;

/// <summary>Датчики адаптера из <c>D3DKMT_ADAPTER_PERFDATA</c> и <c>D3DKMT_ADAPTER_PERFDATACAPS</c>.</summary>
public sealed record KmtPerfData(Reading<double> TemperatureC, Reading<double> TemperatureLimitC, Reading<int> FanRpm,
    Reading<double> MemoryClockMhz);

public interface IKmtPerfDataSource
{
    /// <summary>Не бросает: ошибка графического ядра даёт <c>Failure</c> или <c>Unsupported</c> во всех полях.</summary>
    KmtPerfData Read(AdapterLuid luid);
}
