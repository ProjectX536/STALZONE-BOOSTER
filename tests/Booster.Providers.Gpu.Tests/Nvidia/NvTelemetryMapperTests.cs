using Booster.Core.Abstractions;
using Booster.Core.Resources;
using Booster.Providers.Gpu.Nvidia;

namespace Booster.Providers.Gpu.Tests.Nvidia;

public sealed class NvTelemetryMapperTests
{
    private static NvResult<T> Ok<T>(T value) => new(NvApiStatus.Ok, value);

    [Fact]
    public void Graphics_domain_utilization()
    {
        Assert.Equal(Reading<double>.Known(37, "nvapi"), NvTelemetryMapper.Utilization(Ok<uint?>(37)));
        Assert.Equal(100, NvTelemetryMapper.Utilization(Ok<uint?>(104)).Value);
    }

    [Fact]
    public void Not_present_domain_is_unsupported() =>
        Assert.Equal(DataStatus.NotSupported, NvTelemetryMapper.Utilization(Ok<uint?>(null)).Status);

    [Fact]
    public void Gpu_target_sensor_temperature()
    {
        Assert.Equal(Reading<double>.Known(63, "nvapi"), NvTelemetryMapper.Temperature(Ok<int?>(63)));
        Assert.Equal(DataStatus.Unknown, NvTelemetryMapper.Temperature(Ok<int?>(0)).Status);
        Assert.Equal(DataStatus.NotSupported, NvTelemetryMapper.Temperature(Ok<int?>(null)).Status);
    }

    [Fact]
    public void Zero_clock_is_unknown()
    {
        var (graphics, memory) = NvTelemetryMapper.Clocks(Ok<(uint?, uint?)>((0, null)));

        Assert.Equal(DataStatus.Unknown, graphics.Status);
        Assert.Equal(DataStatus.Unknown, memory.Status);
    }

    [Fact]
    public void Clock_in_mhz()
    {
        var (graphics, memory) = NvTelemetryMapper.Clocks(Ok<(uint?, uint?)>((1_905_000, 7_000_983)));

        Assert.Equal(Reading<double>.Known(1905, "nvapi"), graphics);
        Assert.Equal(7000.983, memory.Value);
    }

    [Theory]
    [InlineData(0x3u, GpuThrottleReasons.Thermal | GpuThrottleReasons.PowerLimit)]
    [InlineData(0x80000000u, GpuThrottleReasons.Other)]
    [InlineData(0u, GpuThrottleReasons.None)]
    [InlineData(0x1Cu, GpuThrottleReasons.PowerSource | GpuThrottleReasons.ApiTriggered | GpuThrottleReasons.InsufficientPower)]
    public void Perf_decrease_flags_map_to_reasons(uint flags, GpuThrottleReasons expected) =>
        Assert.Equal(Reading<GpuThrottleReasons>.Known(expected, "nvapi"), NvTelemetryMapper.Reasons(Ok(flags)));

    [Theory]
    [InlineData(-104, DataStatus.NotSupported)]
    [InlineData(-137, DataStatus.RequiresAdmin)]
    [InlineData(-10, DataStatus.Failed)]
    [InlineData(-1, DataStatus.Failed)]
    public void Status_codes_map_to_reading_status(int status, DataStatus expected)
    {
        var reading = NvTelemetryMapper.Fan(new NvResult<uint>(status, 0));

        Assert.Equal(expected, reading.Status);
        Assert.Equal("nvapi", reading.Source);
        Assert.Contains(NvApiStatus.Name(status), reading.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Zero_fan_has_the_kmt_note() =>
        Assert.Equal(Reading<int>.Unknown("nvapi", ReportStrings.KmtFanZeroRpm), NvTelemetryMapper.Fan(Ok(0u)));

    [Fact]
    public void Unknown_status_code_is_named_by_number() =>
        Assert.Equal("NVAPI status -999", NvApiStatus.Name(-999));
}
