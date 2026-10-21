using Booster.Core.Abstractions;
using Booster.Platform.Windows.Hardware;
using Booster.Platform.Windows.Tests.TestSupport;

namespace Booster.Platform.Windows.Tests.Hardware;

public class OsMapperTests
{
    private static readonly Booster.Platform.Windows.Common.CimRow RealOs = WmiFixture.LoadSingle("desktop-nvidia/Win32_OperatingSystem.json");

    [Fact]
    public void Real_os_with_registry_values_is_mapped()
    {
        var os = OsMapper.Map(RealOs, "25H2", 1234);

        Assert.Equal("Майкрософт Windows 11 Pro", os.Caption.Value);
        Assert.Equal("10.0.26300", os.Version.Value);
        Assert.Equal(26300, os.BuildNumber.Value);
        Assert.Equal(1234, os.Ubr.Value);
        Assert.Equal("25H2", os.DisplayVersion.Value);
        Assert.Equal("64-разрядная", os.Architecture.Value);
        Assert.Equal(DataStatus.Available, os.Ubr.Status);
    }

    [Fact]
    public void Last_boot_time_keeps_offset()
    {
        var boot = OsMapper.Map(RealOs, null, null).LastBootTime;

        Assert.Equal(DataStatus.Available, boot.Status);
        // "20261016160556.500441+180": 500441 мкс = 500 мс + 441 мкс.
        var expected = new DateTimeOffset(2026, 10, 16, 16, 5, 56, 500, 441, TimeSpan.FromHours(3));
        Assert.Equal(expected, boot.Value);
        Assert.Equal(TimeSpan.FromHours(3), boot.Value.Offset);
    }

    [Fact]
    public void Missing_registry_values_are_unknown()
    {
        var os = OsMapper.Map(RealOs, null, null);

        Assert.Equal(DataStatus.Unknown, os.Ubr.Status);
        Assert.Equal(DataStatus.Unknown, os.DisplayVersion.Status);
        Assert.Equal(DataStatus.Available, os.BuildNumber.Status);
    }

    [Theory]
    [InlineData("1234")]
    [InlineData(-1)]
    [InlineData("")]
    public void Ubr_of_unexpected_registry_type_is_unknown(object ubr)
    {
        Assert.Equal(DataStatus.Unknown, OsMapper.Map(RealOs, "25H2", ubr).Ubr.Status);
    }

    [Fact]
    public void Missing_os_row_makes_every_wmi_field_unknown()
    {
        var os = OsMapper.Map(null, null, null);

        Assert.All(new DataStatus[]
        {
            os.Caption.Status, os.Version.Status, os.BuildNumber.Status, os.Ubr.Status,
            os.DisplayVersion.Status, os.Architecture.Status, os.LastBootTime.Status,
        }, s => Assert.Equal(DataStatus.Unknown, s));
    }
}
