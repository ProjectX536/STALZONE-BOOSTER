using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Core.Sensors;

namespace Booster.Core.Tests.TestSupport;

/// <summary>
/// Провайдер GPU в памяти: тест задаёт доступность, возможности и телеметрию, фейк запоминает, о каком адаптере
/// и приложении его спрашивали. <see cref="Failure"/> бросается из чтения возможностей, телеметрии и параметров.
/// </summary>
public sealed class FakeGpuProvider(string providerId, GpuVendor? vendor = null) : IGpuProvider
{
    public const string NvidiaId = "providers.gpu.nvidia";
    public const string AmdId = "providers.gpu.amd";
    public const string GenericId = "providers.gpu.generic";

    private readonly List<GpuAdapterInfo> _telemetryReads = [];
    private readonly List<(GpuAdapterInfo Adapter, string? Application)> _settingsReads = [];

    public string ProviderId => providerId;

    public GpuVendor? Vendor => vendor;

    public bool Available { get; set; } = true;

    public Exception? AvailabilityFailure { get; set; }

    public Exception? Failure { get; set; }

    public IReadOnlyList<CapabilityReport> Capabilities { get; set; } = [];

    public Func<GpuAdapterInfo, GpuTelemetry> Telemetry { get; set; } = _ => GpuTelemetry.AllUnknown(providerId, null);

    public IReadOnlyList<GpuAdapterInfo> TelemetryReads
    {
        get { lock (_telemetryReads) return [.. _telemetryReads]; }
    }

    public IReadOnlyList<(GpuAdapterInfo Adapter, string? Application)> SettingsReads
    {
        get { lock (_settingsReads) return [.. _settingsReads]; }
    }

    public Task<bool> IsAvailableAsync(GpuAdapterInfo adapter, CancellationToken ct) =>
        AvailabilityFailure is { } failure ? Task.FromException<bool>(failure) : Task.FromResult(Available);

    public Task<GpuCapabilities> GetCapabilitiesAsync(GpuAdapterInfo adapter, CancellationToken ct) =>
        Failure is { } failure
            ? Task.FromException<GpuCapabilities>(failure)
            : Task.FromResult(new GpuCapabilities(providerId, Capabilities));

    public Task<GpuTelemetry> ReadTelemetryAsync(GpuAdapterInfo adapter, CancellationToken ct)
    {
        lock (_telemetryReads) _telemetryReads.Add(adapter);
        return Failure is { } failure ? Task.FromException<GpuTelemetry>(failure) : Task.FromResult(Telemetry(adapter));
    }

    public Task<GpuSettingsResult> ReadSettingsAsync(GpuAdapterInfo adapter, string? application, CancellationToken ct)
    {
        lock (_settingsReads) _settingsReads.Add((adapter, application));
        return Failure is { } failure
            ? Task.FromException<GpuSettingsResult>(failure)
            : Task.FromResult(new GpuSettingsResult(providerId, application, application is null ? "Base Profile" : null,
                Reading<IReadOnlyList<GpuSetting>>.Known([], providerId)));
    }
}

/// <summary>Датчики CPU в памяти: считает замеры, <see cref="Failure"/> бросается из <see cref="Sample"/>.</summary>
public sealed class FakeCpuSensorSource : ICpuSensorSource
{
    private int _samples;

    public CpuTelemetry Telemetry { get; set; } = SensorSamples.Cpu();

    public IReadOnlyList<CapabilityReport> Capabilities { get; set; } = [];

    public Exception? Failure { get; set; }

    public int Samples => Volatile.Read(ref _samples);

    public CpuTelemetry Sample()
    {
        Interlocked.Increment(ref _samples);
        return Failure is { } failure ? throw failure : Telemetry;
    }

    public IReadOnlyList<CapabilityReport> DescribeCapabilities() => Capabilities;
}

/// <summary>Детектор GPU с готовым списком адаптеров; считает вызовы.</summary>
public sealed class FakeGpuDetector(params GpuAdapterInfo[] adapters) : IInventoryDetector<GpuInventory>
{
    private int _calls;

    public string SectionId => "gpu";

    public int Calls => Volatile.Read(ref _calls);

    public Task<GpuInventory> DetectAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(new GpuInventory(adapters, Reading<bool>.Unknown(), Reading<IReadOnlyList<AppGpuPreference>>.Unknown()));
    }
}
