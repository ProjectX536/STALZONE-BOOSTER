using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Tests.Common;

public class DmtfDateTests
{
    [Theory]
    [InlineData("20230915000000.******+***")]
    [InlineData("20230915000000.000000-000")]
    [InlineData("20230915")]
    public void Date_is_taken_from_first_eight_digits(string dmtf)
    {
        Assert.Equal(new DateOnly(2023, 9, 15), DmtfDate.ParseDate(dmtf));
    }

    [Theory]
    [InlineData("16010101000000.000000+000")]
    [InlineData("19700101000000.000000+000")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("garbage")]
    [InlineData("2023091")]
    [InlineData("20231345000000.000000+000")]
    [InlineData("********000000.000000+000")]
    public void Placeholder_or_invalid_date_gives_null(string? dmtf)
    {
        Assert.Null(DmtfDate.ParseDate(dmtf));
    }

    [Fact]
    public void Date_time_keeps_fraction_and_offset_in_minutes()
    {
        var parsed = DmtfDate.ParseDateTime("20261016083015.500000+180");

        Assert.Equal(new DateTimeOffset(2026, 10, 16, 8, 30, 15, 500, TimeSpan.FromHours(3)), parsed);
    }

    [Fact]
    public void Negative_offset_and_unknown_fraction_are_supported()
    {
        var parsed = DmtfDate.ParseDateTime("20261016083015.******-300");

        Assert.Equal(new DateTimeOffset(2026, 10, 16, 8, 30, 15, TimeSpan.FromHours(-5)), parsed);
    }

    [Theory]
    [InlineData("20230915000000.000000+***")]
    [InlineData("16010101000000.000000+000")]
    [InlineData("20261016083015.500000+999")]
    [InlineData("20261016083015.500000")]
    [InlineData("20261016083015x500000+180")]
    [InlineData("")]
    [InlineData(null)]
    public void Date_time_without_reliable_instant_gives_null(string? dmtf)
    {
        Assert.Null(DmtfDate.ParseDateTime(dmtf));
    }

    [Fact]
    public void Real_last_boot_time_from_fixture_parses()
    {
        var os = TestSupport.WmiFixture.LoadSingle("desktop-nvidia/Win32_OperatingSystem.json");

        var boot = DmtfDate.ParseDateTime(os.GetString("LastBootUpTime"));

        Assert.NotNull(boot);
        Assert.Equal(TimeSpan.FromHours(3), boot.Value.Offset);
    }
}
