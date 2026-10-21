using System.Globalization;

namespace Booster.Core.Configuration;

/// <summary>
/// Возвращает в допустимые пределы пороги из <c>thresholds.json</c>, с которыми оценка теряет смысл, и сообщает о каждом
/// исправлении предупреждением <see cref="ConfigWarningKind.ValueClamped"/>. Сами пороги — в файле, здесь только границы.
/// </summary>
public static class ThresholdsNormalizer
{
    /// <summary>
    /// Отрицательный запас сдвинул бы предел выше заявленного драйвером; больше 20 °C — «у предела» оказалась бы
    /// обычная рабочая температура под нагрузкой.
    /// </summary>
    private const double MinimumTemperatureMarginC = 0;
    private const double MaximumTemperatureMarginC = 20;

    /// <summary>Ниже 50% GPU простаивает больше, чем работает, и снижение частоты — штатное энергосбережение.</summary>
    private const double MinimumGpuBusyPercent = 50;
    private const double MaximumGpuBusyPercent = 100;

    /// <summary>Меньше 1% — шум измерения частоты; 90% и больше — адаптер почти остановлен, это уже не снижение частоты.</summary>
    private const double MinimumClockDropPercent = 1;
    private const double MaximumClockDropPercent = 90;

    /// <summary>Падение частоты определяется сравнением, а сравнивать один замер не с чем.</summary>
    private const int MinimumSampleCount = 2;

    /// <summary>
    /// Интервал меньше полутора соседних не отличить от обычного разброса; при множителе больше 10 пропускаются заметные зависания.
    /// </summary>
    private const double MinimumStutterFactor = 1.5;
    private const double MaximumStutterFactor = 10;

    /// <summary>Превышение больше 100 мс выше самих рывков, которые замечает игрок.</summary>
    private const double MaximumStutterExcessMs = 100;

    /// <summary>Медиана меньше чем по 5 соседям следует за самим рывком; больше 600 (10 с при 60 FPS) не следует за сценой.</summary>
    private const int MinimumStutterWindow = 5;
    private const int MaximumStutterWindow = 600;

    /// <summary>Доля кадра меньше половины не означает ни «занят», ни «простаивает»: сигнал перестаёт их различать.</summary>
    private const double MinimumFrameShare = 0.5;
    private const double MaximumFrameShare = 1.0;

    /// <summary>Загрузка ниже 50% — ресурс простаивает больше, чем работает, и называть его пределом нельзя.</summary>
    private const double MinimumLoadPercent = 50;
    private const double MaximumLoadPercent = 100;

    /// <summary>
    /// Меньше 0,5% — неточная частота дисплея (59,94 вместо 60) уже выводит FPS из допуска; больше 20% — 48 FPS при 60 Гц
    /// считались бы пределом дисплея.
    /// </summary>
    private const double MinimumRefreshTolerancePercent = 0.5;
    private const double MaximumRefreshTolerancePercent = 20;

    /// <summary>P95 не бывает меньше P50, поэтому отношение меньше 1 не имеет смысла; больше 2 «ровными» были бы и кадры с рывками.</summary>
    private const double MinimumUniformRatio = 1.0;
    private const double MaximumUniformRatio = 2.0;

    /// <summary>Меньше 1% свободной памяти система уже не работает нормально; при 50% свободной нехватки памяти нет.</summary>
    private const double MinimumAvailableMemoryPercent = 1;
    private const double MaximumAvailableMemoryPercent = 50;

    /// <summary>Нулевой порог делает сигналом любое значение: единичное чтение страницы, любую задержку диска, любой рывок.</summary>
    private const double MinimumRateThreshold = 1;

    /// <summary>P99 не бывает меньше P50: при отношении 1 неровным оказался бы любой прогон.</summary>
    private const double MinimumPercentileRatio = 1.1;

    /// <summary>
    /// Доли процента DPC и прерываний есть на любой системе — порог ниже 0,1% ловил бы норму; при четверти всех логических CPU
    /// система уже почти не отвечает, и более высокий порог ничего не ловит.
    /// </summary>
    private const double MinimumLatencyPercent = 0.1;
    private const double MaximumLatencyPercent = 25;

    /// <summary>Сравнению нужен хотя бы один годный прогон; больше 10 — больше, чем прогонов в серии.</summary>
    private const int MinimumComparisonRuns = 1;
    private const int MaximumComparisonRuns = 10;

    /// <summary>Разница больше 50% заметна без всякого порога — порог выше теряет смысл.</summary>
    private const double MaximumChangePercent = 50;

    /// <summary>Меньше 1% всех логических CPU занимает почти любой фоновый процесс.</summary>
    private const double MinimumBackgroundCpuPercent = 1;

    /// <summary>Нулевая задержка и нулевой джиттер в сети не бывают: при пороге 0 сигналом стал бы любой ответ.</summary>
    private const double MinimumNetworkMs = 1;

    /// <summary>Задержка до шлюза больше секунды — уже обрыв связи, а не высокая задержка.</summary>
    private const double MaximumGatewayLatencyMs = 1000;

    /// <summary>Больше 5 с до внешнего узла сетевая игра не работает вовсе.</summary>
    private const double MaximumHostLatencyMs = 5000;

    /// <summary>Джиттер больше секунды — то же, что обрыв связи.</summary>
    private const double MaximumJitterMs = 1000;

    /// <summary>При пороге меньше 1 МБ тяжёлым считался бы любой запущенный процесс.</summary>
    private const double MinimumStartupPrivateMemoryMb = 1;

    /// <summary>4 ГБ частной памяти у фоновой программы — выше уже не фоновая утилита; больший порог отключил бы правило.</summary>
    private const double MaximumStartupPrivateMemoryMb = 4096;

    public static ConfigLoadResult<Thresholds> Normalize(Thresholds t)
    {
        ArgumentNullException.ThrowIfNull(t);
        var warnings = new List<ConfigWarning>();

        T Clamp<T>(T value, T minimum, T maximum, string key)
            where T : struct, IComparable<T>, IFormattable
        {
            var clamped = value.CompareTo(minimum) < 0 ? minimum : value.CompareTo(maximum) > 0 ? maximum : value;
            if (clamped.CompareTo(value) != 0)
            {
                warnings.Add(new(ConfigWarningKind.ValueClamped, ConfigFiles.Thresholds,
                    string.Create(CultureInfo.InvariantCulture, $"{key}: {value} -> {clamped}")));
            }
            return clamped;
        }

        double Share(double value, string key) => Clamp(value, MinimumFrameShare, MaximumFrameShare, "bottleneck." + key);
        double Load(double value, string key) => Clamp(value, MinimumLoadPercent, MaximumLoadPercent, "bottleneck." + key);
        double Rate(double value, string key) => Clamp(value, MinimumRateThreshold, double.MaxValue, "bottleneck." + key);

        var s = t.Throttle;
        var minimumSamples = Clamp(s.MinimumSamples, MinimumSampleCount, int.MaxValue, "throttle.minimumSamples");
        var throttle = new ThrottleSettings(
            Clamp(s.TemperatureMarginC, MinimumTemperatureMarginC, MaximumTemperatureMarginC, "throttle.temperatureMarginC"),
            Clamp(s.GpuBusyUtilizationPercent, MinimumGpuBusyPercent, MaximumGpuBusyPercent, "throttle.gpuBusyUtilizationPercent"),
            Clamp(s.ClockDropPercent, MinimumClockDropPercent, MaximumClockDropPercent, "throttle.clockDropPercent"),
            minimumSamples,
            Clamp(s.SessionWindowSamples, minimumSamples, int.MaxValue, "throttle.sessionWindowSamples"));

        var st = t.Stutter;
        var stutter = new StutterSettings(
            Clamp(st.Factor, MinimumStutterFactor, MaximumStutterFactor, "stutter.factor"),
            Clamp(st.MinimumExcessMs, 0, MaximumStutterExcessMs, "stutter.minimumExcessMs"),
            Clamp(st.WindowFrames, MinimumStutterWindow, MaximumStutterWindow, "stutter.windowFrames"));

        var b = t.Bottleneck;
        var bottleneck = new BottleneckSettings(
            Share(b.GpuBoundFrameShare, "gpuBoundFrameShare"),
            Load(b.GpuBusyPercent, "gpuBusyPercent"),
            Share(b.GpuIdleFrameShare, "gpuIdleFrameShare"),
            Load(b.GpuIdlePercent, "gpuIdlePercent"),
            Load(b.BusyThreadPercent, "busyThreadPercent"),
            Load(b.SystemCpuPercent, "systemCpuPercent"),
            Share(b.CpuBoundFrameShare, "cpuBoundFrameShare"),
            Clamp(b.RefreshTolerancePercent, MinimumRefreshTolerancePercent, MaximumRefreshTolerancePercent,
                "bottleneck.refreshTolerancePercent"),
            Share(b.SyncedFrameShare, "syncedFrameShare"),
            Clamp(b.UniformFrameTimeRatio, MinimumUniformRatio, MaximumUniformRatio, "bottleneck.uniformFrameTimeRatio"),
            Load(b.VramUsedPercent, "vramUsedPercent"),
            Clamp(b.AvailableMemoryPercent, MinimumAvailableMemoryPercent, MaximumAvailableMemoryPercent,
                "bottleneck.availableMemoryPercent"),
            Load(b.CommitPercent, "commitPercent"),
            Rate(b.PageReadsPerSecond, "pageReadsPerSecond"),
            Load(b.DiskBusyPercent, "diskBusyPercent"),
            Rate(b.DiskLatencyMs, "diskLatencyMs"),
            Rate(b.StuttersPerMinute, "stuttersPerMinute"),
            Clamp(b.PercentileRatio, MinimumPercentileRatio, double.MaxValue, "bottleneck.percentileRatio"),
            Clamp(b.DpcTimePercent, MinimumLatencyPercent, MaximumLatencyPercent, "bottleneck.dpcTimePercent"),
            Clamp(b.InterruptTimePercent, MinimumLatencyPercent, MaximumLatencyPercent, "bottleneck.interruptTimePercent"));

        var c = t.Comparison;
        var comparison = new ComparisonSettings(
            Clamp(c.MinimumRuns, MinimumComparisonRuns, MaximumComparisonRuns, "comparison.minimumRuns"),
            Clamp(c.MinimumChangePercent, 0, MaximumChangePercent, "comparison.minimumChangePercent"),
            Clamp(c.RunVariationPercent, 0, double.MaxValue, "comparison.runVariationPercent"),
            Clamp(c.MinimumStutterChangePerMinute, 0, double.MaxValue, "comparison.minimumStutterChangePerMinute"));

        var recommendations = new RecommendationSettings(Clamp(t.Recommendations.BackgroundCpuPercent, MinimumBackgroundCpuPercent,
            MaximumLoadPercent, "recommendations.backgroundCpuPercent"));

        var memory = new MemoryThresholds(Clamp(t.Memory.CommitPercent, MinimumLoadPercent, MaximumLoadPercent, "memory.commitPercent"));

        var n = t.Network;
        var network = new NetworkThresholds(
            Clamp(n.GatewayLatencyMs, MinimumNetworkMs, MaximumGatewayLatencyMs, "network.gatewayLatencyMs"),
            Clamp(n.HostLatencyMs, MinimumNetworkMs, MaximumHostLatencyMs, "network.hostLatencyMs"),
            Clamp(n.JitterMs, MinimumNetworkMs, MaximumJitterMs, "network.jitterMs"),
            Clamp(n.LossPercent, 0, 100, "network.lossPercent"));

        var startup = new StartupThresholds(Clamp(t.Startup.MinPrivateMemoryMb, MinimumStartupPrivateMemoryMb,
            MaximumStartupPrivateMemoryMb, "startup.minPrivateMemoryMb"));

        return warnings.Count == 0
            ? new(t, [])
            : new(t with
            {
                Throttle = throttle, Stutter = stutter, Bottleneck = bottleneck, Comparison = comparison, Recommendations = recommendations,
                Memory = memory, Network = network, Startup = startup,
            }, warnings);
    }
}
