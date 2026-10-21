namespace Booster.Core.Configuration;

/// <summary>Содержимое <c>settings.json</c>. Значения и их обоснования — в самом файле.</summary>
public sealed record BoosterSettings(
    int SchemaVersion,
    MonitoringSettings Monitoring,
    DetectorSettings Detectors,
    PresentMonSettings PresentMon,
    BenchmarkSettings Benchmark,
    LoggingSettings Logging,
    JournalSettings Journal,
    DiagnosticsSettings Diagnostics,
    NetworkSettings Network)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record MonitoringSettings(int SelfUsageIntervalMs, int GpuEngineSampleIntervalMs, int GameDetectionIntervalMs,
    int SensorIntervalMs, int GameExitConfirmationCount);

public sealed record DetectorSettings(int DefaultTimeoutMs, IReadOnlyDictionary<string, int> TimeoutsMs)
{
    public TimeSpan TimeoutFor(string sectionId) =>
        TimeSpan.FromMilliseconds(TimeoutsMs.TryGetValue(sectionId, out var ms) ? ms : DefaultTimeoutMs);
}

/// <summary><see cref="ExecutablePath"/> = null — искать PresentMon в стандартных местах.</summary>
public sealed record PresentMonSettings(string? ExecutablePath, string SessionName);

/// <summary>Серия бенчмарка (раздел <c>benchmark</c>): расписание прогонов, звуковые сигналы, хранение и замер в игровой сессии.</summary>
/// <param name="RunCount">Прогонов в серии.</param>
/// <param name="RunSeconds">Длительность одного прогона, с.</param>
/// <param name="PauseSeconds">Пауза между прогонами, с.</param>
/// <param name="StartDelaySeconds">Задержка от запуска PresentMon до первого прогона, с.</param>
/// <param name="SoundCues">Звук в начале и в конце каждого прогона.</param>
/// <param name="RetainedSeriesCount">Сколько последних серий хранится на диске.</param>
/// <param name="SessionCaptureSeconds">Замер кадров в начале игровой сессии, с (<c>collectFrameMetrics</c>).</param>
public sealed record BenchmarkSettings(int RunCount, int RunSeconds, int PauseSeconds, int StartDelaySeconds, bool SoundCues,
    int RetainedSeriesCount, int SessionCaptureSeconds);

public sealed record LoggingSettings(int RetainedFileCount);

public sealed record JournalSettings(int RetainedSessionCount);

/// <summary>Системная диагностика (раздел <c>diagnostics</c>).</summary>
/// <param name="StabilityDays">За сколько дней читается журнал стабильности.</param>
/// <param name="MaxStabilityEvents">Предел числа событий журнала за одно чтение; остальные не читаются.</param>
/// <param name="TopProcessCount">Сколько процессов показывать в списках по памяти и вводу-выводу.</param>
/// <param name="ActivityIntervalMs">Опрос фоновой активности на экране «Система», мс.</param>
public sealed record DiagnosticsSettings(int StabilityDays, int MaxStabilityEvents, int TopProcessCount, int ActivityIntervalMs);

/// <summary>Тест сети по запросу (раздел <c>network</c>).</summary>
/// <param name="PingCount">Число раундов: в каждом раунде — по одному эхо-запросу каждой цели.</param>
/// <param name="PingIntervalMs">Пауза между раундами, мс.</param>
/// <param name="PingTimeoutMs">Ожидание ответа, мс.</param>
/// <param name="Host">Дополнительный узел теста; null — только шлюз.</param>
public sealed record NetworkSettings(int PingCount, int PingIntervalMs, int PingTimeoutMs, string? Host);
