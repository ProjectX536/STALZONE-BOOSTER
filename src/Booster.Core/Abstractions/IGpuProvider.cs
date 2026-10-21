using Booster.Core.Inventory;

namespace Booster.Core.Abstractions;

public interface IGpuProvider
{
    string ProviderId { get; }

    GpuVendor? Vendor { get; }

    Task<bool> IsAvailableAsync(GpuAdapterInfo adapter, CancellationToken ct);

    Task<GpuCapabilities> GetCapabilitiesAsync(GpuAdapterInfo adapter, CancellationToken ct);

    Task<GpuTelemetry> ReadTelemetryAsync(GpuAdapterInfo adapter, CancellationToken ct);

    Task<GpuSettingsResult> ReadSettingsAsync(GpuAdapterInfo adapter, string? application, CancellationToken ct);
}

public sealed record GpuCapabilities(string ProviderId, IReadOnlyList<CapabilityReport> Capabilities);

public sealed record GpuTelemetry(
    Reading<double> UtilizationPercent,
    Reading<double> TemperatureC,
    Reading<double> TemperatureLimitC,
    Reading<double> CoreClockMhz,
    Reading<double> MemoryClockMhz,
    Reading<long> VramUsedBytes,
    Reading<long> VramTotalBytes,
    Reading<double> PowerWatts,
    Reading<int> FanRpm,
    Reading<GpuThrottleReasons> ThrottleReasons)
{
    public static GpuTelemetry AllUnknown(string? source, string? note) => new(
        Reading<double>.Unknown(source, note),
        Reading<double>.Unknown(source, note),
        Reading<double>.Unknown(source, note),
        Reading<double>.Unknown(source, note),
        Reading<double>.Unknown(source, note),
        Reading<long>.Unknown(source, note),
        Reading<long>.Unknown(source, note),
        Reading<double>.Unknown(source, note),
        Reading<int>.Unknown(source, note),
        Reading<GpuThrottleReasons>.Unknown(source, note));

    public static GpuTelemetry AllFailed(string source, string note) => new(
        Reading<double>.Failure(source, note),
        Reading<double>.Failure(source, note),
        Reading<double>.Failure(source, note),
        Reading<double>.Failure(source, note),
        Reading<double>.Failure(source, note),
        Reading<long>.Failure(source, note),
        Reading<long>.Failure(source, note),
        Reading<double>.Failure(source, note),
        Reading<int>.Failure(source, note),
        Reading<GpuThrottleReasons>.Failure(source, note));
}

[Flags]
public enum GpuThrottleReasons
{
    None = 0,
    Thermal = 1,
    PowerLimit = 2,
    PowerSource = 4,
    ApiTriggered = 8,
    InsufficientPower = 16,
    Other = 32,
}

public enum GpuSettingOrigin
{
    DriverDefault,
    UserChanged,
    NotSet,
    Unknown,
}

public sealed record GpuSetting(string Id, string DisplayName, Reading<string> CurrentValue, GpuSettingOrigin Origin, bool IsReadOnly);

public sealed record GpuSettingsResult(string ProviderId, string? Application, string? ProfileName, Reading<IReadOnlyList<GpuSetting>> Settings);
