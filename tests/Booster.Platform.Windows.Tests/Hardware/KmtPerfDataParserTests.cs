using Booster.Core.Abstractions;
using Booster.Core.Resources;
using Booster.Platform.Windows.Hardware;

namespace Booster.Platform.Windows.Tests.Hardware;

public sealed class KmtPerfDataParserTests
{
    private static readonly (uint TemperatureMaxDeciC, uint TemperatureWarningDeciC)? Caps = (1050, 830);

    private static KmtPerfData Parse(ulong memoryFrequencyHz = 7_001_000_000, uint fanRpm = 1450, uint temperatureDeciC = 653,
        bool poweredOn = true, (uint, uint)? caps = null) =>
        KmtPerfDataParser.Parse(memoryFrequencyHz, fanRpm, temperatureDeciC, poweredOn, caps ?? Caps);

    [Fact]
    public void Temperature_in_deci_celsius()
    {
        Assert.Equal(Reading<double>.Known(65.3, KmtPerfDataParser.Source), Parse(temperatureDeciC: 653).TemperatureC);
    }

    [Fact]
    public void Zero_temperature_is_unknown()
    {
        var temperature = Parse(temperatureDeciC: 0).TemperatureC;

        Assert.Equal(DataStatus.Unknown, temperature.Status);
        Assert.Equal(KmtPerfDataParser.Source, temperature.Source);
        Assert.Equal(ReportStrings.KmtTemperatureNotReported, temperature.Note);
    }

    [Fact]
    public void Zero_temperature_when_powered_off_says_so()
    {
        var temperature = Parse(temperatureDeciC: 0, poweredOn: false).TemperatureC;

        Assert.Equal(DataStatus.Unknown, temperature.Status);
        Assert.Equal(ReportStrings.KmtAdapterPoweredDown, temperature.Note);
    }

    [Fact]
    public void Limit_comes_from_temperature_warning()
    {
        Assert.Equal(Reading<double>.Known(83.0, KmtPerfDataParser.Source), Parse(caps: (1050, 830)).TemperatureLimitC);
    }

    [Fact]
    public void Zero_warning_gives_unknown_limit()
    {
        var limit = Parse(caps: (1050, 0)).TemperatureLimitC;

        Assert.Equal(DataStatus.Unknown, limit.Status);
        Assert.Equal(ReportStrings.KmtTemperatureLimitNotReported, limit.Note);
    }

    [Fact]
    public void Missing_caps_give_unknown_limit()
    {
        var data = KmtPerfDataParser.Parse(7_001_000_000, 1450, 653, poweredOn: true, caps: null);

        Assert.Equal(DataStatus.Unknown, data.TemperatureLimitC.Status);
        Assert.Equal(ReportStrings.KmtTemperatureLimitNotReported, data.TemperatureLimitC.Note);
        Assert.True(data.TemperatureC.IsAvailable);
    }

    [Fact]
    public void Zero_fan_is_unknown_with_note()
    {
        var fan = Parse(fanRpm: 0).FanRpm;

        Assert.Equal(DataStatus.Unknown, fan.Status);
        Assert.Equal(ReportStrings.KmtFanZeroRpm, fan.Note);
        Assert.Equal(Reading<int>.Known(1450, KmtPerfDataParser.Source), Parse(fanRpm: 1450).FanRpm);
    }

    [Fact]
    public void Memory_frequency_in_mhz()
    {
        Assert.Equal(Reading<double>.Known(7001, KmtPerfDataParser.Source), Parse(memoryFrequencyHz: 7_001_000_000).MemoryClockMhz);
        Assert.Equal(DataStatus.Unknown, Parse(memoryFrequencyHz: 0).MemoryClockMhz.Status);
    }

    [Fact]
    public void Not_supported_status_gives_unsupported_fields()
    {
        var data = KmtPerfDataParser.FromStatus("D3DKMTQueryAdapterInfo(KMTQAITYPE_ADAPTERPERFDATA)", unchecked((int)0xC00000BB));

        Assert.All(Fields(data), r =>
        {
            Assert.Equal(DataStatus.NotSupported, r.Status);
            Assert.Equal(KmtPerfDataParser.Source, r.Source);
            Assert.Contains("0xC00000BB", r.Note, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Other_failure_status_gives_failure_with_hex_code()
    {
        var data = KmtPerfDataParser.FromStatus("D3DKMTOpenAdapterFromLuid", unchecked((int)0xC000000D));

        Assert.All(Fields(data), r =>
        {
            Assert.Equal(DataStatus.Failed, r.Status);
            Assert.Equal("D3DKMTOpenAdapterFromLuid failed: NTSTATUS 0xC000000D", r.Note);
        });
    }

    private static IEnumerable<(DataStatus Status, string? Source, string? Note)> Fields(KmtPerfData data) =>
    [
        (data.TemperatureC.Status, data.TemperatureC.Source, data.TemperatureC.Note),
        (data.TemperatureLimitC.Status, data.TemperatureLimitC.Source, data.TemperatureLimitC.Note),
        (data.FanRpm.Status, data.FanRpm.Source, data.FanRpm.Note),
        (data.MemoryClockMhz.Status, data.MemoryClockMhz.Source, data.MemoryClockMhz.Note),
    ];
}
