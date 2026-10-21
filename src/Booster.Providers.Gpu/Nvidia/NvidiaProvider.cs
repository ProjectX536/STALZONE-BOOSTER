using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Booster.Providers.Gpu.Resources;
using Microsoft.Extensions.Logging;

namespace Booster.Providers.Gpu.Nvidia;

/// <summary>
/// NVIDIA через NVAPI: загрузка, температура, частоты, вентилятор и причины снижения частоты; параметры драйвера (DRS)
/// только для чтения. <see cref="INvApi"/> создаётся один раз и лениво; неудача создания запоминается до перезапуска.
/// HANDLE_INVALIDATED (сброс драйвера) — библиотека пересоздаётся при следующем чтении. Методы не бросают исключений.
/// </summary>
internal sealed class NvidiaProvider(INvApiFactory factory, ILogger<NvidiaProvider> logger) : IGpuProvider
{
    public const string Id = "providers.gpu.nvidia";

    private readonly Lock _lock = new();
    private INvApi? _api;
    private int? _createStatus;
    private IReadOnlyList<NvPhysicalGpu>? _gpus;

    public string ProviderId => Id;

    public GpuVendor? Vendor => GpuVendor.Nvidia;

    public Task<bool> IsAvailableAsync(GpuAdapterInfo adapter, CancellationToken ct) =>
        Task.FromResult(Guard(() => Resolve(adapter).Gpu is not null, false, "IsAvailable"));

    public Task<GpuCapabilities> GetCapabilitiesAsync(GpuAdapterInfo adapter, CancellationToken ct) =>
        Task.FromResult(new GpuCapabilities(Id, Guard(() => Capabilities(adapter), ex => CapabilitiesOnError(adapter, ex), "GetCapabilities")));

    public Task<GpuTelemetry> ReadTelemetryAsync(GpuAdapterInfo adapter, CancellationToken ct) =>
        Task.FromResult(Guard(() =>
        {
            var (api, gpu, note) = Resolve(adapter);
            return api is null || gpu is null ? GpuTelemetry.AllUnknown(Id, note) : ReadTelemetry(api, gpu.Handle);
        }, ex => GpuTelemetry.AllFailed(NvTelemetryMapper.Source, FailedNote(ex)), "ReadTelemetry"));

    public Task<GpuSettingsResult> ReadSettingsAsync(GpuAdapterInfo adapter, string? application, CancellationToken ct) =>
        Task.FromResult(Guard(() =>
        {
            var api = Api(out var note);
            if (api is null)
                return new GpuSettingsResult(Id, application, null, Reading<IReadOnlyList<GpuSetting>>.Unsupported(Id, note));

            var drs = ReadDrs(api, application);
            if (!drs.Ok)
                return new GpuSettingsResult(Id, application, null, NvTelemetryMapper.FromStatus<IReadOnlyList<GpuSetting>>(drs.Status));

            var values = drs.Value!.Values;
            IReadOnlyList<GpuSetting> settings = NvDrsSettingCatalog.Entries
                .Where(e => application is null || values.ContainsKey(e.SettingId))
                .Select(e => NvDrsSettingCatalog.Describe(e, values.GetValueOrDefault(e.SettingId)))
                .ToArray();
            return new GpuSettingsResult(Id, application, drs.Value.ProfileName,
                Reading<IReadOnlyList<GpuSetting>>.Known(settings, NvTelemetryMapper.Source));
        }, ex => new GpuSettingsResult(Id, application, null,
            Reading<IReadOnlyList<GpuSetting>>.Failure(NvTelemetryMapper.Source, FailedNote(ex))), "ReadSettings"));

    /// <summary>
    /// Адаптер — область адаптера: <c>gpu.vendor-api</c> и поля телеметрии по пробному чтению. Система —
    /// <c>gpu.settings.read</c> по чтению глобального профиля DRS.
    /// </summary>
    private IReadOnlyList<CapabilityReport> Capabilities(GpuAdapterInfo adapter)
    {
        var reports = new List<CapabilityReport>();
        var (api, gpu, note) = Resolve(adapter);
        if (adapter.Luid is { IsAvailable: true, Value: var luid })
        {
            var scope = CapabilityScopes.Adapter(luid);
            if (api is null || gpu is null)
            {
                reports.Add(new(CapabilityIds.GpuVendorApi, scope, CapabilityStatus.NotSupported, Id, note));
            }
            else
            {
                reports.Add(new(CapabilityIds.GpuVendorApi, scope, CapabilityStatus.Supported, Id, VendorApiNote(api)));
                var probe = ReadTelemetry(api, gpu.Handle);
                CapabilityReport FromReading<T>(string id, Reading<T> reading) => new(id, scope, reading.Status switch
                {
                    DataStatus.Available => CapabilityStatus.Supported,
                    DataStatus.RequiresAdmin => CapabilityStatus.RequiresAdmin,
                    DataStatus.Failed => CapabilityStatus.Unknown,
                    _ => CapabilityStatus.NotSupported,
                }, Id, reading.IsAvailable ? null : reading.Note);

                reports.Add(FromReading(CapabilityIds.GpuUtilization, probe.UtilizationPercent));
                reports.Add(FromReading(CapabilityIds.GpuTemperature, probe.TemperatureC));
                reports.Add(FromReading(CapabilityIds.GpuCoreClock, probe.CoreClockMhz));
                reports.Add(FromReading(CapabilityIds.GpuMemoryClock, probe.MemoryClockMhz));
                reports.Add(FromReading(CapabilityIds.GpuFan, probe.FanRpm));
                reports.Add(FromReading(CapabilityIds.GpuThrottleReasonsSource, probe.ThrottleReasons));
            }
        }

        var drsApi = Api(out var apiNote);
        var drs = drsApi is null ? null : (NvResult<NvDrsRead>?)ReadDrs(drsApi, null);
        reports.Add(drs is { Ok: true }
            ? new(CapabilityIds.GpuSettingsRead, CapabilityScopes.System, CapabilityStatus.Supported, Id, null)
            : new(CapabilityIds.GpuSettingsRead, CapabilityScopes.System, CapabilityStatus.NotSupported, Id,
                drs is { } failed ? NvApiStatus.Name(failed.Status) : apiNote));
        return reports;
    }

    private IReadOnlyList<CapabilityReport> CapabilitiesOnError(GpuAdapterInfo adapter, Exception ex) =>
        [new(CapabilityIds.GpuVendorApi, adapter.Luid is { IsAvailable: true, Value: var luid } ? CapabilityScopes.Adapter(luid) : CapabilityScopes.System,
            CapabilityStatus.Unknown, Id, FailedNote(ex))];

    private GpuTelemetry ReadTelemetry(INvApi api, nint gpu)
    {
        var telemetry = NvTelemetryMapper.Read(api, gpu, out var invalidated);
        if (invalidated)
            Reset();
        return telemetry;
    }

    private NvResult<NvDrsRead> ReadDrs(INvApi api, string? application)
    {
        var drs = api.ReadDrs(application, NvDrsSettingCatalog.SettingIds);
        if (drs.Status == NvApiStatus.HandleInvalidated)
            Reset();
        return drs;
    }

    /// <summary>GPU NVAPI для адаптера Windows; note — почему не найден.</summary>
    private (INvApi? Api, NvPhysicalGpu? Gpu, string Note) Resolve(GpuAdapterInfo adapter)
    {
        var api = Api(out var note);
        if (api is null)
            return (null, null, note);

        if (_gpus is null)
        {
            var enumerated = api.EnumeratePhysicalGpus();
            if (!enumerated.Ok)
            {
                if (enumerated.Status == NvApiStatus.HandleInvalidated)
                    Reset();
                return (null, null, Unavailable(enumerated.Status));
            }
            _gpus = enumerated.Value!;
        }

        var gpu = Match(adapter, _gpus);
        return (api, gpu, gpu is null ? ProviderStrings.NvidiaNoAdapterMatch : string.Empty);
    }

    /// <summary>
    /// По LUID; если LUID адаптера или GPU NVAPI неизвестен — по PCI (DeviceId и SubsystemId), только среди GPU без
    /// LUID и только когда кандидат ровно один. Порядок перечисления NVAPI и DXGI не совпадает, по нему не сопоставляем.
    /// </summary>
    internal static NvPhysicalGpu? Match(GpuAdapterInfo adapter, IReadOnlyList<NvPhysicalGpu> gpus)
    {
        if (adapter.Luid is { IsAvailable: true, Value: var luid } && gpus.FirstOrDefault(g => g.Luid is { IsAvailable: true } l && l.Value == luid) is { } byLuid)
            return byLuid;

        if (adapter.Pci is not { IsAvailable: true, Value: var pci })
            return null;
        var candidates = gpus
            .Where(g => !g.Luid.IsAvailable && g.Pci is { IsAvailable: true } p && p.Value.DeviceId == pci.DeviceId && p.Value.SubsystemId == pci.SubsystemId)
            .Take(2)
            .ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private INvApi? Api(out string note)
    {
        note = string.Empty;
        if (_api is null && _createStatus is null)
        {
            _api = factory.TryCreate(out var status);
            if (_api is null)
            {
                _createStatus = status;
                logger.LogInformation("NVAPI недоступен: {Status}", NvApiStatus.Name(status));
            }
        }
        if (_api is null)
            note = Unavailable(_createStatus ?? NvApiStatus.LibraryNotFound);
        return _api;
    }

    /// <summary>Дескрипторы недействительны: библиотека освобождается и создаётся заново при следующем чтении.</summary>
    private void Reset()
    {
        logger.LogInformation("NVAPI: HANDLE_INVALIDATED, библиотека будет создана заново");
        _api?.Dispose();
        _api = null;
        _gpus = null;
    }

    private T Guard<T>(Func<T> body, T fallback, string operation) => Guard(body, _ => fallback, operation);

    /// <summary>Под одной блокировкой; исключение — в журнал и показания-ошибки, а не наружу.</summary>
    private T Guard<T>(Func<T> body, Func<Exception, T> fallback, string operation)
    {
        lock (_lock)
        {
            try
            {
                return body();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "NVAPI: ошибка в {Operation}", operation);
                return fallback(ex);
            }
        }
    }

    private static string VendorApiNote(INvApi api) => string.IsNullOrEmpty(api.DriverVersion)
        ? "NVAPI"
        : Format(ProviderStrings.NvidiaVendorApiFormat, api.DriverVersion);

    private static string Unavailable(int status) => Format(ProviderStrings.NvidiaVendorApiUnavailableFormat, NvApiStatus.Name(status));

    private static string FailedNote(Exception ex) => Format(ProviderStrings.NvidiaReadFailedFormat, ex.Message);

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}
