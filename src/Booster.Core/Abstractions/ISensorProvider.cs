using Booster.Core.Inventory;
using Booster.Core.Sensors;

namespace Booster.Core.Abstractions;

/// <summary>Датчики GPU и CPU через провайдеры: вендорский и общий для каждого адаптера, CPU — отдельно.</summary>
public interface ISensorProvider
{
    /// <summary>Возможности в реестр, один замер и параметры драйвера (глобальные и для applications). Запоминает адаптеры.</summary>
    Task<SensorSurvey> SurveyAsync(IReadOnlyList<GpuAdapterInfo> adapters, IReadOnlyList<string> applications, CancellationToken ct);

    /// <summary>Лёгкий замер. Адаптеры — из последнего SurveyAsync; без него — из IInventoryDetector&lt;GpuInventory&gt;.</summary>
    Task<SensorSnapshot> ReadAsync(CancellationToken ct);
}
