using Booster.Core.Abstractions;
using Booster.Core.Inventory;

namespace Booster.Platform.Windows.Monitoring;

public sealed record GpuCounterSample(Reading<double> UtilizationPercent, Reading<long> DedicatedUsageBytes);

/// <summary>Загрузка и занятая видеопамять каждого адаптера по системным счётчикам; подменяется в тестах.</summary>
public interface IGpuSystemCounterSource
{
    /// <summary>Загрузка — за время с прошлого вызова; при первом вызове Unknown.</summary>
    IReadOnlyDictionary<AdapterLuid, GpuCounterSample> Sample();

    /// <summary>Категории счётчиков есть в системе и читаются; <c>Failure</c> — с текстом ошибки.</summary>
    Reading<bool> IsAvailable { get; }
}
