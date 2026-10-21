using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Core.Resources;
using Microsoft.Extensions.Logging;

namespace Booster.Core.Sensors;

public sealed partial class SensorProvider(
    IInventoryDetector<GpuInventory> gpuDetector,
    IEnumerable<IGpuProvider> providers,
    ICpuSensorSource cpu,
    ICapabilityRegistry capabilities,
    TimeProvider time,
    ILogger<SensorProvider> logger) : ISensorProvider
{
    public const string NoProviderId = "none";

    private const string CpuSource = "sensors.cpu";

    private readonly IReadOnlyList<IGpuProvider> _providers = [.. providers];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<PolledAdapter>? _polled;

    public async Task<SensorSurvey> SurveyAsync(IReadOnlyList<GpuAdapterInfo> adapters, IReadOnlyList<string> applications, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(applications);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var polled = await SelectProvidersAsync(adapters, ct).ConfigureAwait(false);
            _polled = polled;
            await ReportCapabilitiesAsync(polled, ct).ConfigureAwait(false);
            var sensors = await SampleAsync(polled, ct).ConfigureAwait(false);
            var settings = await ReadSettingsAsync(polled, applications, ct).ConfigureAwait(false);
            return new SensorSurvey(sensors, settings);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SensorSnapshot> ReadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_polled is null)
            {
                var inventory = await gpuDetector.DetectAsync(ct).ConfigureAwait(false);
                _polled = await SelectProvidersAsync(inventory.Adapters, ct).ConfigureAwait(false);
            }

            return await SampleAsync(_polled, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record PolledAdapter(GpuAdapterInfo Adapter, IGpuProvider? Vendor, IGpuProvider? Generic, IReadOnlyList<IGpuProvider> Matching);

    private async Task<IReadOnlyList<PolledAdapter>> SelectProvidersAsync(IReadOnlyList<GpuAdapterInfo> adapters, CancellationToken ct)
    {
        var generic = _providers.Where(p => p.Vendor is null).ToList();
        var polled = new List<PolledAdapter>(adapters.Count);
        foreach (var adapter in adapters)
        {
            if (adapter.IsSoftware)
                continue;
            if (!adapter.Luid.IsAvailable)
            {
                LogNoLuid(adapter.Name);
                continue;
            }

            var vendorProviders = _providers.Where(p => p.Vendor == adapter.Vendor).ToList();
            IGpuProvider? vendor = null;
            foreach (var provider in vendorProviders)
            {
                if (await IsAvailableAsync(provider, adapter, ct).ConfigureAwait(false))
                {
                    vendor = provider;
                    break;
                }
            }

            polled.Add(new PolledAdapter(adapter, vendor, generic.FirstOrDefault(), [.. vendorProviders, .. generic]));
        }

        return polled;
    }

    private async Task<bool> IsAvailableAsync(IGpuProvider provider, GpuAdapterInfo adapter, CancellationToken ct)
    {
        try
        {
            return await provider.IsAvailableAsync(adapter, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsCancellation(ex, ct))
        {
            LogProviderFailed(ex, provider.ProviderId, adapter.Name, "availability");
            return false;
        }
    }

    private async Task ReportCapabilitiesAsync(IReadOnlyList<PolledAdapter> polled, CancellationToken ct)
    {
        var best = new Dictionary<(string Id, string Scope), CapabilityReport>();
        foreach (var entry in polled)
        {
            foreach (var provider in entry.Matching)
            {
                GpuCapabilities reported;
                try
                {
                    reported = await provider.GetCapabilitiesAsync(entry.Adapter, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (!IsCancellation(ex, ct))
                {
                    LogProviderFailed(ex, provider.ProviderId, entry.Adapter.Name, "capabilities");
                    continue;
                }

                foreach (var report in reported.Capabilities)
                {
                    var key = (report.CapabilityId, report.Scope);
                    if (!best.TryGetValue(key, out var current) || Rank(report.Status) > Rank(current.Status))
                        best[key] = report;
                }
            }
        }

        foreach (var report in best.Values)
            capabilities.Report(report);

        try
        {
            foreach (var report in cpu.DescribeCapabilities())
                capabilities.Report(report);
        }
        catch (Exception ex)
        {
            LogCpuFailed(ex);
        }
    }

    private static int Rank(CapabilityStatus status) => status switch
    {
        CapabilityStatus.Supported => 3,
        CapabilityStatus.RequiresAdmin => 2,
        CapabilityStatus.NotSupported => 1,
        _ => 0,
    };

    private async Task<SensorSnapshot> SampleAsync(IReadOnlyList<PolledAdapter> polled, CancellationToken ct)
    {
        var takenAt = time.GetUtcNow();
        var gpus = new List<GpuSensorReading>(polled.Count);
        foreach (var entry in polled)
        {
            var vendor = entry.Vendor is null ? null : await ReadTelemetryAsync(entry.Vendor, entry.Adapter, ct).ConfigureAwait(false);
            var generic = entry.Generic is null ? null : await ReadTelemetryAsync(entry.Generic, entry.Adapter, ct).ConfigureAwait(false);
            var telemetry = (vendor, generic) switch
            {
                ({ } v, { } g) => GpuTelemetryMerger.Merge(v, g),
                ({ } v, null) => v,
                (null, { } g) => g,
                _ => GpuTelemetry.AllUnknown(null, ReportStrings.SensorNoProvider),
            };
            gpus.Add(new GpuSensorReading(entry.Adapter, entry.Vendor?.ProviderId ?? entry.Generic?.ProviderId ?? NoProviderId, telemetry));
        }

        return new SensorSnapshot(takenAt, gpus, SampleCpu());
    }

    private async Task<GpuTelemetry> ReadTelemetryAsync(IGpuProvider provider, GpuAdapterInfo adapter, CancellationToken ct)
    {
        try
        {
            return await provider.ReadTelemetryAsync(adapter, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsCancellation(ex, ct))
        {
            LogProviderFailed(ex, provider.ProviderId, adapter.Name, "telemetry");
            return GpuTelemetry.AllFailed(provider.ProviderId, FailureNote(ex));
        }
    }

    private CpuTelemetry SampleCpu()
    {
        try
        {
            return cpu.Sample();
        }
        catch (Exception ex)
        {
            LogCpuFailed(ex);
            var failed = Reading<double>.Failure(CpuSource, FailureNote(ex));
            return new CpuTelemetry(failed, failed, failed, failed, failed);
        }
    }

    private async Task<IReadOnlyList<GpuSettingsResult>> ReadSettingsAsync(IReadOnlyList<PolledAdapter> polled,
        IReadOnlyList<string> applications, CancellationToken ct)
    {
        var vendors = new List<(IGpuProvider Provider, GpuAdapterInfo Adapter)>();
        foreach (var entry in polled)
        {
            if (entry.Vendor is { } provider && !vendors.Exists(v => ReferenceEquals(v.Provider, provider)))
                vendors.Add((provider, entry.Adapter));
        }

        var results = new List<GpuSettingsResult>();
        foreach (var application in (IEnumerable<string?>)[null, .. applications])
        {
            foreach (var (provider, adapter) in vendors)
                results.Add(await ReadSettingsAsync(provider, adapter, application, ct).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<GpuSettingsResult> ReadSettingsAsync(IGpuProvider provider, GpuAdapterInfo adapter, string? application,
        CancellationToken ct)
    {
        try
        {
            return await provider.ReadSettingsAsync(adapter, application, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsCancellation(ex, ct))
        {
            LogProviderFailed(ex, provider.ProviderId, adapter.Name, "settings");
            return new GpuSettingsResult(provider.ProviderId, application, null,
                Reading<IReadOnlyList<GpuSetting>>.Failure(provider.ProviderId, FailureNote(ex)));
        }
    }

    private static bool IsCancellation(Exception ex, CancellationToken ct) => ex is OperationCanceledException && ct.IsCancellationRequested;

    private static string FailureNote(Exception ex) =>
        string.Format(CultureInfo.CurrentCulture, ReportStrings.SensorProviderFailedFormat, ex.GetType().Name);

    [LoggerMessage(Level = LogLevel.Information, Message = "GPU adapter {Adapter} has no LUID; its sensors are not polled")]
    private partial void LogNoLuid(string adapter);

    [LoggerMessage(Level = LogLevel.Warning, Message = "GPU provider {ProviderId} failed to read {What} for {Adapter}")]
    private partial void LogProviderFailed(Exception ex, string providerId, string adapter, string what);

    [LoggerMessage(Level = LogLevel.Warning, Message = "CPU sensors failed")]
    private partial void LogCpuFailed(Exception ex);
}
