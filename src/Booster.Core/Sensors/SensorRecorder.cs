using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Sensors;

/// <summary>
/// Запись датчиков на время замера: первый замер сразу, далее раз в monitoring.sensorIntervalMs до отмены.
/// Неудачный замер пропускается с записью в лог; отмена — штатный конец записи, а не ошибка.
/// </summary>
public sealed partial class SensorRecorder(ISensorProvider sensors, IBoosterConfiguration config, TimeProvider time,
    ILogger<SensorRecorder> logger)
{
    public async Task<IReadOnlyList<SensorSnapshot>> RecordAsync(CancellationToken stop)
    {
        var samples = new List<SensorSnapshot>();
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(config.Current.Settings.Monitoring.SensorIntervalMs), time);
            do
            {
                try
                {
                    samples.Add(await sensors.ReadAsync(stop).ConfigureAwait(false));
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && stop.IsCancellationRequested))
                {
                    LogReadFailed(ex, samples.Count);
                }
            }
            while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Замер окончен — собранного достаточно.
        }

        return samples;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sensor read failed; skipped after {Collected} samples")]
    private partial void LogReadFailed(Exception ex, int collected);
}
