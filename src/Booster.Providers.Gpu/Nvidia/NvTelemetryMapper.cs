using Booster.Core.Abstractions;
using Booster.Core.Resources;
using Booster.Providers.Gpu.Resources;

namespace Booster.Providers.Gpu.Nvidia;

internal static class NvTelemetryMapper
{
    public const string Source = NvApiLibrary.Source;

    public static GpuTelemetry Read(INvApi api, nint gpu, out bool invalidated)
    {
        var utilization = api.GetGraphicsUtilizationPercent(gpu);
        var temperature = api.GetGpuTemperatureC(gpu);
        var clocks = api.GetCurrentClocks(gpu);
        var fan = api.GetFanRpm(gpu);
        var reasons = api.GetPerfDecreaseFlags(gpu);
        invalidated = new[] { utilization.Status, temperature.Status, clocks.Status, fan.Status, reasons.Status }
            .Contains(NvApiStatus.HandleInvalidated);

        var (graphics, memory) = Clocks(clocks);
        return new GpuTelemetry(
            Utilization(utilization),
            Temperature(temperature),
            Reading<double>.Unknown(Source),
            graphics,
            memory,
            Reading<long>.Unknown(Source),
            Reading<long>.Unknown(Source),
            Reading<double>.Unknown(Source),
            Fan(fan),
            Reasons(reasons));
    }

    public static Reading<double> Utilization(NvResult<uint?> result) => !result.Ok
        ? FromStatus<double>(result.Status)
        : result.Value is { } percent
            ? Reading<double>.Known(Math.Min(percent, 100u), Source)
            : Reading<double>.Unsupported(Source, ProviderStrings.NvidiaDomainNotPresent);

    public static Reading<double> Temperature(NvResult<int?> result) => !result.Ok
        ? FromStatus<double>(result.Status)
        : result.Value switch
        {
            null => Reading<double>.Unsupported(Source, ProviderStrings.NvidiaNoGpuSensor),
            <= 0 => Reading<double>.Unknown(Source),
            var celsius => Reading<double>.Known(celsius.Value, Source),
        };

    public static (Reading<double> Graphics, Reading<double> Memory) Clocks(NvResult<(uint? GraphicsKhz, uint? MemoryKhz)> result) =>
        result.Ok
            ? (Clock(result.Value.GraphicsKhz), Clock(result.Value.MemoryKhz))
            : (FromStatus<double>(result.Status), FromStatus<double>(result.Status));

    public static Reading<GpuThrottleReasons> Reasons(NvResult<uint> result) =>
        result.Ok ? Reading<GpuThrottleReasons>.Known(Reasons(result.Value), Source) : FromStatus<GpuThrottleReasons>(result.Status);

    public static GpuThrottleReasons Reasons(uint flags)
    {
        var reasons = GpuThrottleReasons.None;
        if ((flags & NvPerfDecrease.Thermal) != 0) reasons |= GpuThrottleReasons.Thermal;
        if ((flags & NvPerfDecrease.PowerControl) != 0) reasons |= GpuThrottleReasons.PowerLimit;
        if ((flags & NvPerfDecrease.AcBattery) != 0) reasons |= GpuThrottleReasons.PowerSource;
        if ((flags & NvPerfDecrease.ApiTriggered) != 0) reasons |= GpuThrottleReasons.ApiTriggered;
        if ((flags & NvPerfDecrease.InsufficientPower) != 0) reasons |= GpuThrottleReasons.InsufficientPower;
        if ((flags & NvPerfDecrease.Unknown) != 0) reasons |= GpuThrottleReasons.Other;
        return reasons;
    }

    public static Reading<int> Fan(NvResult<uint> result) => !result.Ok
        ? FromStatus<int>(result.Status)
        : result.Value == 0
            ? Reading<int>.Unknown(Source, ReportStrings.KmtFanZeroRpm)
            : Reading<int>.Known((int)Math.Min(result.Value, int.MaxValue), Source);

    public static Reading<T> FromStatus<T>(int status) => status switch
    {
        NvApiStatus.NotSupported => Reading<T>.Unsupported(Source, NvApiStatus.Name(status)),
        NvApiStatus.InvalidUserPrivilege => Reading<T>.NeedsAdmin(Source, NvApiStatus.Name(status)),
        _ => Reading<T>.Failure(Source, NvApiStatus.Name(status)),
    };

    private static Reading<double> Clock(uint? khz) => khz is { } value and > 0
        ? Reading<double>.Known(value / 1000.0, Source)
        : Reading<double>.Unknown(Source, ProviderStrings.NvidiaNoClock);
}
