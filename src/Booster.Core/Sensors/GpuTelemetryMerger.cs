using Booster.Core.Abstractions;

namespace Booster.Core.Sensors;

/// <summary>
/// Сливает телеметрию вендорского и общего провайдера поле за полем: известное значение вендора, иначе известное
/// значение общего, иначе причина вендора (например, нужны права администратора), иначе — что сообщил общий.
/// </summary>
public static class GpuTelemetryMerger
{
    public static GpuTelemetry Merge(GpuTelemetry vendor, GpuTelemetry generic)
    {
        ArgumentNullException.ThrowIfNull(vendor);
        ArgumentNullException.ThrowIfNull(generic);
        return new(
            Pick(vendor.UtilizationPercent, generic.UtilizationPercent),
            Pick(vendor.TemperatureC, generic.TemperatureC),
            Pick(vendor.TemperatureLimitC, generic.TemperatureLimitC),
            Pick(vendor.CoreClockMhz, generic.CoreClockMhz),
            Pick(vendor.MemoryClockMhz, generic.MemoryClockMhz),
            Pick(vendor.VramUsedBytes, generic.VramUsedBytes),
            Pick(vendor.VramTotalBytes, generic.VramTotalBytes),
            Pick(vendor.PowerWatts, generic.PowerWatts),
            Pick(vendor.FanRpm, generic.FanRpm),
            Pick(vendor.ThrottleReasons, generic.ThrottleReasons));
    }

    private static Reading<T> Pick<T>(Reading<T> vendor, Reading<T> generic)
    {
        if (vendor.IsAvailable)
            return vendor;
        if (generic.IsAvailable)
            return generic;
        return vendor.Status != DataStatus.Unknown ? vendor : generic;
    }
}
