namespace Booster.Core.Configuration;

/// <summary>
/// Поднимает до допустимого минимума значения, которые нарушили бы требования или сломали бы работу,
/// и сообщает о каждом исправлении предупреждением <see cref="ConfigWarningKind.ValueClamped"/>.
/// </summary>
public static class SettingsNormalizer
{
    /// <summary>docs/PRINCIPLES.md: собственный мониторинг — не чаще раза в секунду.</summary>
    private const int MinimumMonitoringIntervalMs = 1000;

    /// <summary>
    /// Нижние границы корректности, а не настраиваемые пороги: таймаут короче секунды не вмещает
    /// ни одного WMI-запроса, а хранение логов, сессий или серий меньше одной единицы теряет смысл.
    /// </summary>
    private const int MinimumTimeoutMs = 1000;
    private const int MinimumRetainedFiles = 1;
    private const int MinimumRetainedSessions = 1;
    private const int MinimumRetainedSeries = 1;

    /// <summary>Серия хотя бы из одного прогона; больше 10 прогонов одной сцены подряд пользователь не выдерживает.</summary>
    public const int MinimumRunCount = 1;
    public const int MaximumRunCount = 10;

    /// <summary>
    /// Меньше 10 с при 30 FPS не набирает 300 кадров, нужных для 1% low; больше 10 минут — прогоны одной сцены
    /// перестают быть похожими.
    /// </summary>
    public const int MinimumRunSeconds = 10;
    public const int MaximumRunSeconds = 600;

    /// <summary>0 — прогоны подряд; больше 5 минут паузы — серия теряет смысл повторения.</summary>
    public const int MinimumPauseSeconds = 0;
    public const int MaximumPauseSeconds = 300;

    /// <summary>ETW-сессия PresentMon запускается около секунды, и нужно время переключиться в игру.</summary>
    public const int MinimumStartDelaySeconds = 3;
    public const int MaximumStartDelaySeconds = 300;

    /// <summary>Меньше 30 с — не набрать данных для метрик; больше 30 минут — CSV в сотни мегабайт.</summary>
    private const int MinimumSessionCaptureSeconds = 30;
    private const int MaximumSessionCaptureSeconds = 1800;

    /// <summary>
    /// Нужен хотя бы один замер без процесса игры; больше 10 замеров (20 с при опросе раз в 2 с) — изменения
    /// держатся заметно дольше самой игры.
    /// </summary>
    private const int MinimumGameExitConfirmations = 1;
    private const int MaximumGameExitConfirmations = 10;

    /// <summary>Хотя бы последние сутки; больше 90 дней — события, которые уже не относятся к текущему состоянию системы.</summary>
    private const int MinimumStabilityDays = 1;
    private const int MaximumStabilityDays = 90;

    /// <summary>Меньше 10 событий не показывают картину; больше 5000 — чтение журнала занимает заметное время.</summary>
    private const int MinimumStabilityEvents = 10;
    private const int MaximumStabilityEvents = 5000;

    /// <summary>Меньше 3 процессов — не список; больше 50 не помещается на экран и в отчёт.</summary>
    private const int MinimumTopProcessCount = 3;
    private const int MaximumTopProcessCount = 50;

    /// <summary>Меньше 5 ответов — медиана и джиттер случайны; больше 600 — тест дольше 10 минут при опросе раз в секунду.</summary>
    private const int MinimumPingCount = 5;
    private const int MaximumPingCount = 600;

    /// <summary>Чаще 5 запросов в секунду — уже нагрузка на роутер; реже раза в 10 с тест растягивается без пользы.</summary>
    private const int MinimumPingIntervalMs = 200;
    private const int MaximumPingIntervalMs = 10_000;

    /// <summary>Меньше 100 мс не дождаться ответа далёкого узла; больше 5 с ответ уже бесполезен для игры.</summary>
    private const int MinimumPingTimeoutMs = 100;
    private const int MaximumPingTimeoutMs = 5000;

    public static ConfigLoadResult<BoosterSettings> Normalize(BoosterSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var warnings = new List<ConfigWarning>();

        int AtLeast(int value, int minimum, string key)
        {
            if (value >= minimum)
                return value;
            warnings.Add(new(ConfigWarningKind.ValueClamped, ConfigFiles.Settings, $"{key}: {value} -> {minimum}"));
            return minimum;
        }

        int Between(int value, int minimum, int maximum, string key)
        {
            if (value <= maximum)
                return AtLeast(value, minimum, key);
            warnings.Add(new(ConfigWarningKind.ValueClamped, ConfigFiles.Settings, $"{key}: {value} -> {maximum}"));
            return maximum;
        }

        var monitoring = new MonitoringSettings(
            AtLeast(s.Monitoring.SelfUsageIntervalMs, MinimumMonitoringIntervalMs, "monitoring.selfUsageIntervalMs"),
            AtLeast(s.Monitoring.GpuEngineSampleIntervalMs, MinimumMonitoringIntervalMs, "monitoring.gpuEngineSampleIntervalMs"),
            AtLeast(s.Monitoring.GameDetectionIntervalMs, MinimumMonitoringIntervalMs, "monitoring.gameDetectionIntervalMs"),
            AtLeast(s.Monitoring.SensorIntervalMs, MinimumMonitoringIntervalMs, "monitoring.sensorIntervalMs"),
            Between(s.Monitoring.GameExitConfirmationCount, MinimumGameExitConfirmations, MaximumGameExitConfirmations,
                "monitoring.gameExitConfirmationCount"));

        var timeouts = s.Detectors.TimeoutsMs
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ToDictionary(p => p.Key, p => AtLeast(p.Value, MinimumTimeoutMs, $"detectors.timeoutsMs.{p.Key}"));
        var detectors = new DetectorSettings(
            AtLeast(s.Detectors.DefaultTimeoutMs, MinimumTimeoutMs, "detectors.defaultTimeoutMs"), timeouts);

        var b = s.Benchmark;
        var benchmark = b with
        {
            RunCount = Between(b.RunCount, MinimumRunCount, MaximumRunCount, "benchmark.runCount"),
            RunSeconds = Between(b.RunSeconds, MinimumRunSeconds, MaximumRunSeconds, "benchmark.runSeconds"),
            PauseSeconds = Between(b.PauseSeconds, MinimumPauseSeconds, MaximumPauseSeconds, "benchmark.pauseSeconds"),
            StartDelaySeconds = Between(b.StartDelaySeconds, MinimumStartDelaySeconds, MaximumStartDelaySeconds, "benchmark.startDelaySeconds"),
            RetainedSeriesCount = AtLeast(b.RetainedSeriesCount, MinimumRetainedSeries, "benchmark.retainedSeriesCount"),
            SessionCaptureSeconds = Between(b.SessionCaptureSeconds, MinimumSessionCaptureSeconds, MaximumSessionCaptureSeconds,
                "benchmark.sessionCaptureSeconds"),
        };
        var logging = new LoggingSettings(
            AtLeast(s.Logging.RetainedFileCount, MinimumRetainedFiles, "logging.retainedFileCount"));
        var journal = new JournalSettings(
            AtLeast(s.Journal.RetainedSessionCount, MinimumRetainedSessions, "journal.retainedSessionCount"));

        var d = s.Diagnostics;
        var diagnostics = new DiagnosticsSettings(
            Between(d.StabilityDays, MinimumStabilityDays, MaximumStabilityDays, "diagnostics.stabilityDays"),
            Between(d.MaxStabilityEvents, MinimumStabilityEvents, MaximumStabilityEvents, "diagnostics.maxStabilityEvents"),
            Between(d.TopProcessCount, MinimumTopProcessCount, MaximumTopProcessCount, "diagnostics.topProcessCount"),
            AtLeast(d.ActivityIntervalMs, MinimumMonitoringIntervalMs, "diagnostics.activityIntervalMs"));

        var n = s.Network;
        var network = new NetworkSettings(
            Between(n.PingCount, MinimumPingCount, MaximumPingCount, "network.pingCount"),
            Between(n.PingIntervalMs, MinimumPingIntervalMs, MaximumPingIntervalMs, "network.pingIntervalMs"),
            Between(n.PingTimeoutMs, MinimumPingTimeoutMs, MaximumPingTimeoutMs, "network.pingTimeoutMs"),
            NetworkHost(n.Host, warnings));

        if (warnings.Count == 0 && network == n)
            return new(s, []);
        return new(s with
        {
            Monitoring = monitoring, Detectors = detectors, Benchmark = benchmark, Logging = logging, Journal = journal,
            Diagnostics = diagnostics, Network = network,
        }, warnings);
    }

    /// <summary>Пробелы обрезаются, пустое значение — «не задан». Не имя DNS и не IP-адрес — удаляется с предупреждением.</summary>
    private static string? NetworkHost(string? host, List<ConfigWarning> warnings)
    {
        var trimmed = host?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        if (Uri.CheckHostName(trimmed) != UriHostNameType.Unknown)
            return trimmed;
        warnings.Add(new(ConfigWarningKind.InvalidValue, ConfigFiles.Settings, $"network.host: '{trimmed}' is not a host name or IP address"));
        return null;
    }
}
