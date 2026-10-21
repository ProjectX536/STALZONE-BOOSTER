using Booster.Core.Abstractions;
using Booster.Core.Sensors;

namespace Booster.Core.Tests.Sensors;

public sealed class GpuTelemetryMergerTests
{
    private const string Vendor = "providers.gpu.nvidia";
    private const string Generic = "providers.gpu.generic";

    [Fact]
    public void Vendor_value_wins_when_known()
    {
        var vendor = GpuTelemetry.AllUnknown(Vendor, null) with { TemperatureC = Reading<double>.Known(71, Vendor) };
        var generic = GpuTelemetry.AllUnknown(Generic, null) with { TemperatureC = Reading<double>.Known(70, Generic) };

        var merged = GpuTelemetryMerger.Merge(vendor, generic);

        Assert.Equal(Reading<double>.Known(71, Vendor), merged.TemperatureC);
    }

    [Fact]
    public void Generic_fills_unknown_vendor_fields()
    {
        var vendor = GpuTelemetry.AllUnknown(Vendor, null) with { TemperatureC = Reading<double>.Known(71, Vendor) };
        var generic = GpuTelemetry.AllUnknown(Generic, null) with
        {
            UtilizationPercent = Reading<double>.Known(97, Generic),
            VramUsedBytes = Reading<long>.Known(6L << 30, Generic),
        };

        var merged = GpuTelemetryMerger.Merge(vendor, generic);

        Assert.Equal(Reading<double>.Known(71, Vendor), merged.TemperatureC);
        Assert.Equal(Reading<double>.Known(97, Generic), merged.UtilizationPercent);
        Assert.Equal(Reading<long>.Known(6L << 30, Generic), merged.VramUsedBytes);
        Assert.Equal(DataStatus.Unknown, merged.FanRpm.Status);
    }

    [Fact]
    public void Vendor_reason_is_kept_when_neither_is_known()
    {
        var vendor = GpuTelemetry.AllUnknown(Vendor, null) with { PowerWatts = Reading<double>.NeedsAdmin(Vendor, "admin only") };
        var generic = GpuTelemetry.AllUnknown(Generic, null) with { FanRpm = Reading<int>.Unsupported(Generic, "no fan counter") };

        var merged = GpuTelemetryMerger.Merge(vendor, generic);

        Assert.Equal(Reading<double>.NeedsAdmin(Vendor, "admin only"), merged.PowerWatts);
        Assert.Equal(Reading<int>.Unsupported(Generic, "no fan counter"), merged.FanRpm);
    }

    [Fact]
    public void All_unknown_carries_source_and_note_in_every_field()
    {
        var telemetry = GpuTelemetry.AllUnknown(Generic, "adapter not found");

        Assert.Equal(Reading<double>.Unknown(Generic, "adapter not found"), telemetry.CoreClockMhz);
        Assert.Equal(Reading<GpuThrottleReasons>.Unknown(Generic, "adapter not found"), telemetry.ThrottleReasons);
        Assert.Equal(Reading<long>.Unknown(Generic, "adapter not found"), telemetry.VramTotalBytes);
    }
}
