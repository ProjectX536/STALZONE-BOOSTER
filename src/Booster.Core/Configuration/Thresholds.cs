namespace Booster.Core.Configuration;

/// <summary>Содержимое <c>thresholds.json</c>. Обоснование каждого порога — комментарий в файле.</summary>
public sealed record Thresholds(
    int SchemaVersion,
    int MinimumFramesForMetrics,
    double LowFreeSpacePercent,
    ThrottleSettings Throttle,
    StutterSettings Stutter,
    BottleneckSettings Bottleneck,
    ComparisonSettings Comparison,
    RecommendationSettings Recommendations,
    MemoryThresholds Memory,
    NetworkThresholds Network,
    StartupThresholds Startup)
{
    public const int CurrentSchemaVersion = 1;
}

/// <summary>Пороги оценки снижения частоты GPU по датчикам (раздел <c>throttle</c>).</summary>
/// <param name="TemperatureMarginC">Запас к пределу температуры драйвера, °C.</param>
/// <param name="GpuBusyUtilizationPercent">Загрузка, начиная с которой падение частоты не объясняется энергосбережением.</param>
/// <param name="ClockDropPercent">Падение частоты от максимума того же прогона под нагрузкой, %.</param>
/// <param name="MinimumSamples">Меньше замеров под нагрузкой — вывода нет.</param>
/// <param name="SessionWindowSamples">Окно оценки в игровой сессии, замеров.</param>
public sealed record ThrottleSettings(
    double TemperatureMarginC,
    double GpuBusyUtilizationPercent,
    double ClockDropPercent,
    int MinimumSamples,
    int SessionWindowSamples);

/// <summary>Определение рывка (раздел <c>stutter</c>; docs/ARCHITECTURE.md, раздел 10).</summary>
/// <param name="Factor">Во сколько раз интервал длиннее медианы соседних.</param>
/// <param name="MinimumExcessMs">На сколько миллисекунд интервал длиннее медианы соседних.</param>
/// <param name="WindowFrames">Число соседних интервалов для медианы.</param>
public sealed record StutterSettings(double Factor, double MinimumExcessMs, int WindowFrames);

/// <summary>
/// Пороги сигналов анализа ограничений (раздел <c>bottleneck</c>). Доли кадра — от 0 до 1, загрузка — в процентах.
/// </summary>
public sealed record BottleneckSettings(
    double GpuBoundFrameShare,
    double GpuBusyPercent,
    double GpuIdleFrameShare,
    double GpuIdlePercent,
    double BusyThreadPercent,
    double SystemCpuPercent,
    double CpuBoundFrameShare,
    double RefreshTolerancePercent,
    double SyncedFrameShare,
    double UniformFrameTimeRatio,
    double VramUsedPercent,
    double AvailableMemoryPercent,
    double CommitPercent,
    double PageReadsPerSecond,
    double DiskBusyPercent,
    double DiskLatencyMs,
    double StuttersPerMinute,
    double PercentileRatio,
    double DpcTimePercent,
    double InterruptTimePercent);

/// <summary>Сравнение серий (раздел <c>comparison</c>).</summary>
/// <param name="MinimumRuns">Меньше годных прогонов в серии — вывод «не доказано».</param>
/// <param name="MinimumChangePercent">Меньшая разница медиан — «без изменений».</param>
/// <param name="RunVariationPercent">Больший разброс среднего FPS между прогонами серии — оговорка.</param>
/// <param name="MinimumStutterChangePerMinute">Меньшая разница рывков в минуту — «без изменений».</param>
public sealed record ComparisonSettings(int MinimumRuns, double MinimumChangePercent, double RunVariationPercent,
    double MinimumStutterChangePerMinute);

/// <summary>Рекомендации (раздел <c>recommendations</c>).</summary>
/// <param name="BackgroundCpuPercent">Фоновый процесс с такой загрузкой всех логических CPU называется в рекомендации.</param>
public sealed record RecommendationSettings(double BackgroundCpuPercent);

/// <summary>Память (раздел <c>memory</c>) для рекомендаций по снимку системы.</summary>
/// <param name="CommitPercent">Выделенная память от предела, %, с которой нужна рекомендация.</param>
public sealed record MemoryThresholds(double CommitPercent);

/// <summary>Тест сети (раздел <c>network</c>).</summary>
/// <param name="GatewayLatencyMs">Медиана задержки до шлюза, мс.</param>
/// <param name="HostLatencyMs">Медиана задержки до внешнего узла, мс.</param>
/// <param name="JitterMs">Среднее изменение задержки между соседними ответами, мс.</param>
/// <param name="LossPercent">Доля потерянных запросов, %.</param>
public sealed record NetworkThresholds(double GatewayLatencyMs, double HostLatencyMs, double JitterMs, double LossPercent);

/// <summary>Автозагрузка (раздел <c>startup</c>).</summary>
/// <param name="MinPrivateMemoryMb">Элемент автозагрузки предлагается отключить, только если его процесс держит не меньше частной памяти, МБ.</param>
public sealed record StartupThresholds(double MinPrivateMemoryMb);
