using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Sensors;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Benchmark;

public sealed record BenchmarkSample(DateTimeOffset At, long Timestamp, SensorSnapshot? Sensors, ProcessActivity? Activity,
    SystemPressure? Pressure);

public sealed partial class BenchmarkRecorder(ISensorProvider sensors, IProcessActivitySource activity, ISystemPressureSource pressure,
    IBoosterConfiguration config, TimeProvider time, ILogger<BenchmarkRecorder> logger)
{
    public const int ActivityTopCount = 10;

    private const string ActivityPart = "process activity";
    private const string PressurePart = "system pressure";
    private const string SensorsPart = "sensors";

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
            // Серия окончена.
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
        if (failedParts.Add(part))
            LogPartFailed(ex, part, collected);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Benchmark {Part} read failed after {Collected} samples; the part stays empty in failed samples")]
    private partial void LogPartFailed(Exception ex, string part, int collected);
}
