using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Sensors;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Benchmark;

/// <summary>Один замер сигналов. Timestamp — TimeProvider.GetTimestamp(): тот же QPC, что у кадров PresentMon.</summary>
/// <param name="Sensors">null — датчики не ответили.</param>
/// <param name="Activity">null — снимок процессов не получен.</param>
/// <param name="Pressure">null — давление памяти и диска не прочитано.</param>
public sealed record BenchmarkSample(DateTimeOffset At, long Timestamp, SensorSnapshot? Sensors, ProcessActivity? Activity,
    SystemPressure? Pressure);

/// <summary>
/// Запись сигналов на время серии: датчики, загрузка процессов, давление памяти и диска. Сбой одного источника
/// не останавливает запись: его часть замера — null, предупреждение в лог — один раз на источник.
/// </summary>
public sealed partial class BenchmarkRecorder(ISensorProvider sensors, IProcessActivitySource activity, ISystemPressureSource pressure,
    IBoosterConfiguration config, TimeProvider time, ILogger<BenchmarkRecorder> logger)
{
    /// <summary>
    /// Посторонних процессов в замере. Больше, чем попадает в сводку: процесс, который иногда выпадает из топа,
    /// не должен терять эти замеры в медиане.
    /// </summary>
    public const int ActivityTopCount = 10;

    private const string ActivityPart = "process activity";
    private const string PressurePart = "system pressure";
    private const string SensorsPart = "sensors";

    /// <summary>Как SensorRecorder: первый замер сразу, далее раз в monitoring.sensorIntervalMs до отмены. Сбой источника — null в замере.</summary>
    /// <param name="processName">Цель — ровно одно из имени и PID.</param>
    public async Task<IReadOnlyList<BenchmarkSample>> RecordAsync(string? processName, int? processId, CancellationToken stop)
    {
        if (string.IsNullOrWhiteSpace(processName) == processId is null)
            throw new ArgumentException("Exactly one of the process name and the process ID is required.", nameof(processName));

        var samples = new List<BenchmarkSample>();
        var failedParts = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(config.Current.Settings.Monitoring.SensorIntervalMs), time);
            do
            {
                var at = time.GetUtcNow();
                var timestamp = time.GetTimestamp();
                var processActivity = Read(() => activity.Sample(processName, processId, ActivityTopCount), ActivityPart, failedParts, samples.Count);
                var systemPressure = Read(pressure.Sample, PressurePart, failedParts, samples.Count);

                SensorSnapshot? snapshot = null;
                try
                {
                    snapshot = await sensors.ReadAsync(stop).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && stop.IsCancellationRequested))
                {
                    ReportFailure(ex, SensorsPart, failedParts, samples.Count);
                }

                samples.Add(new BenchmarkSample(at, timestamp, snapshot, processActivity, systemPressure));
            }
            while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Серия окончена — собранного достаточно.
        }
        return samples;
    }

    private T? Read<T>(Func<T> read, string part, HashSet<string> failedParts, int collected) where T : class
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            ReportFailure(ex, part, failedParts, collected);
            return null;
        }
    }

    private void ReportFailure(Exception ex, string part, HashSet<string> failedParts, int collected)
    {
        // Замер идёт каждые 1–2 с всю серию: одно и то же предупреждение повторять незачем.
        if (failedParts.Add(part))
            LogPartFailed(ex, part, collected);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Benchmark {Part} read failed after {Collected} samples; the part stays empty in failed samples")]
    private partial void LogPartFailed(Exception ex, string part, int collected);
}
