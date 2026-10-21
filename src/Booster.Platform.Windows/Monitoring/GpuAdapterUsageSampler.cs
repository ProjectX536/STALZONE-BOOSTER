using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Microsoft.Extensions.Logging;

namespace Booster.Platform.Windows.Monitoring;

public interface IGpuAdapterUsageSampler
{
    /// <summary>
    /// Опрашивает счётчики GPU Engine процесса до отмены и возвращает адаптер с наибольшей 3D-нагрузкой.
    /// Отмена — штатный конец опроса, а не исключение; сбой счётчиков — <see cref="DataStatus.Failed"/>.
    /// </summary>
    Task<Reading<AdapterLuid>> SampleUntilCancelledAsync(int processId, CancellationToken ct);
}

public sealed partial class GpuAdapterUsageSampler(
    IGpuEngineCounterSource source,
    IBoosterConfiguration config,
    ICapabilityRegistry capabilities,
    TimeProvider time,
    ILogger<GpuAdapterUsageSampler> logger) : IGpuAdapterUsageSampler
{
    public const string ProviderId = "platform.windows.gpu-engine";

    public async Task<Reading<AdapterLuid>> SampleUntilCancelledAsync(int processId, CancellationToken ct)
    {
        var samples = new List<IReadOnlyList<GpuEngineSample>>();
        try
        {
            using var timer = new PeriodicTimer(
                TimeSpan.FromMilliseconds(config.Current.Settings.Monitoring.GpuEngineSampleIntervalMs), time);
            do
            {
                samples.Add(source.Sample(processId));
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Захват кадров закончился — собранного достаточно.
        }
        catch (Exception ex)
        {
            var reason = $"{ex.GetType().Name}: {ex.Message}";
            LogSamplingFailed(processId, reason);
            capabilities.Report(new CapabilityReport(CapabilityIds.GpuEngineCounters, CapabilityScopes.System,
                CapabilityStatus.Unknown, ProviderId, reason));
            return Reading<AdapterLuid>.Failure(GpuAdapterUsageAggregator.Source, reason);
        }

        capabilities.Report(new CapabilityReport(CapabilityIds.GpuEngineCounters, CapabilityScopes.System,
            CapabilityStatus.Supported, ProviderId, null));
        return GpuAdapterUsageAggregator.DominantRenderAdapter(samples);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "GPU Engine counters for process {ProcessId} failed: {Reason}")]
    private partial void LogSamplingFailed(int processId, string reason);
}
