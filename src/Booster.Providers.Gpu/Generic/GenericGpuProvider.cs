using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Hardware;
using Booster.Platform.Windows.Monitoring;
using Booster.Providers.Gpu.Resources;

namespace Booster.Providers.Gpu.Generic;

/// <summary>
/// Общий провайдер для любого аппаратного адаптера: загрузка и занятая видеопамять — системные счётчики, температура,
/// предел, вентилятор и частота памяти — D3DKMT. Частоты ядра, мощности и причин снижения частоты у Windows нет.
/// </summary>
public sealed class GenericGpuProvider(IGpuSystemCounterSource counters, IKmtPerfDataSource kmt) : IGpuProvider
{
    public const string Id = "providers.gpu.generic";

    private readonly Lock _lock = new();
    private IReadOnlyDictionary<AdapterLuid, GpuCounterSample>? _sample;
    private readonly HashSet<AdapterLuid> _served = [];

    public string ProviderId => Id;

    public GpuVendor? Vendor => null;

    public Task<bool> IsAvailableAsync(GpuAdapterInfo adapter, CancellationToken ct) =>
        Task.FromResult(adapter.Luid.IsAvailable && !adapter.IsSoftware);

    /// <summary>
    /// Пробное чтение. Загрузка поддерживается, если адаптер есть в счётчиках: в первом снимке её ещё нет, но это
    /// нехватка второго чтения, а не источника. Поле известно — Supported; ошибка чтения — Unknown; иначе — NotSupported.
    /// </summary>
    public Task<GpuCapabilities> GetCapabilitiesAsync(GpuAdapterInfo adapter, CancellationToken ct)
    {
        if (adapter.Luid is not { IsAvailable: true, Value: var luid })
            return Task.FromResult(new GpuCapabilities(Id, []));

        var entry = Peek(luid);
        var perf = kmt.Read(luid);
        var scope = CapabilityScopes.Adapter(luid);
        CapabilityReport Report(string id, CapabilityStatus status, string? note) => new(id, scope, status, Id, note);
        CapabilityReport FromReading<T>(string id, Reading<T> reading) => Report(id, reading.Status switch
        {
            DataStatus.Available => CapabilityStatus.Supported,
            DataStatus.RequiresAdmin => CapabilityStatus.RequiresAdmin,
            DataStatus.Failed => CapabilityStatus.Unknown,
            _ => CapabilityStatus.NotSupported,
        }, reading.IsAvailable ? null : reading.Note);

        return Task.FromResult(new GpuCapabilities(Id,
        [
            entry is null
                ? Report(CapabilityIds.GpuUtilization, CapabilityStatus.NotSupported, ProviderStrings.GenericNoCounterEntry)
                : Report(CapabilityIds.GpuUtilization, CapabilityStatus.Supported, null),
            entry is null
                ? Report(CapabilityIds.GpuVram, CapabilityStatus.NotSupported, ProviderStrings.GenericNoCounterEntry)
                : FromReading(CapabilityIds.GpuVram, entry.DedicatedUsageBytes),
            FromReading(CapabilityIds.GpuTemperature, perf.TemperatureC),
            FromReading(CapabilityIds.GpuFan, perf.FanRpm),
            FromReading(CapabilityIds.GpuMemoryClock, perf.MemoryClockMhz),
            Report(CapabilityIds.GpuCoreClock, CapabilityStatus.NotSupported, ProviderStrings.GenericCoreClockUnavailable),
            Report(CapabilityIds.GpuPower, CapabilityStatus.NotSupported, ProviderStrings.GenericPowerUnavailable),
            Report(CapabilityIds.GpuThrottleReasonsSource, CapabilityStatus.NotSupported, ProviderStrings.GenericThrottleReasonsVendorOnly),
        ]));
    }

    public Task<GpuTelemetry> ReadTelemetryAsync(GpuAdapterInfo adapter, CancellationToken ct)
    {
        if (adapter.Luid is not { IsAvailable: true, Value: var luid })
            return Task.FromResult(GpuTelemetry.AllUnknown(Id, ProviderStrings.GenericNoLuid));

        var entry = Next(luid);
        var perf = kmt.Read(luid);
        return Task.FromResult(new GpuTelemetry(
            entry?.UtilizationPercent ?? Reading<double>.Unknown(GpuSystemCounterSource.UtilizationSource, ProviderStrings.GenericNoCounterEntry),
            perf.TemperatureC,
            perf.TemperatureLimitC,
            Reading<double>.Unsupported(Id, ProviderStrings.GenericCoreClockUnavailable),
            perf.MemoryClockMhz,
            entry?.DedicatedUsageBytes ?? Reading<long>.Unknown(GpuSystemCounterSource.MemorySource, ProviderStrings.GenericNoCounterEntry),
            adapter.DedicatedVideoMemoryBytes,
            Reading<double>.Unsupported(Id, ProviderStrings.GenericPowerUnavailable),
            perf.FanRpm,
            Reading<GpuThrottleReasons>.Unsupported(Id, ProviderStrings.GenericThrottleReasonsVendorOnly)));
    }

    /// <summary>У Windows нет параметров драйвера для чтения; параметры ОС (HAGS и т. п.) — Этап 5.</summary>
    public Task<GpuSettingsResult> ReadSettingsAsync(GpuAdapterInfo adapter, string? application, CancellationToken ct) =>
        Task.FromResult(new GpuSettingsResult(Id, application, null, Reading<IReadOnlyList<GpuSetting>>.Known([], Id)));

    /// <summary>Текущий снимок счётчиков без начала нового круга: пробному чтению нужна только запись адаптера.</summary>
    private GpuCounterSample? Peek(AdapterLuid luid)
    {
        lock (_lock)
        {
            _sample ??= counters.Sample();
            return _sample.GetValueOrDefault(luid);
        }
    }

    /// <summary>
    /// Один снимок счётчиков на круг опроса всех адаптеров: загрузка — разность с прошлым снимком, и второй вызов
    /// <see cref="IGpuSystemCounterSource.Sample"/> сразу после первого дал бы интервал в миллисекунды. Адаптер, уже
    /// получивший данные из текущего снимка, начинает следующий круг.
    /// </summary>
    private GpuCounterSample? Next(AdapterLuid luid)
    {
        lock (_lock)
        {
            if (_sample is null || !_served.Add(luid))
            {
                _sample = counters.Sample();
                _served.Clear();
                _served.Add(luid);
            }
            return _sample.GetValueOrDefault(luid);
        }
    }
}
