using Booster.Core.Inventory;

namespace Booster.Core.Tests.Inventory;

public class AdapterLuidTests
{
    [Fact]
    public void ToString_matches_gpu_engine_counter_format()
    {
        Assert.Equal("0x00000000_0x0000C2A1", new AdapterLuid(0x0000C2A1, 0).ToString());
    }

    [Fact]
    public void TryParse_round_trips_formatted_value()
    {
        var luid = new AdapterLuid(0x0000C2A1, 0);

        Assert.True(AdapterLuid.TryParse(luid.ToString(), out var parsed));
        Assert.Equal(luid, parsed);
    }

    [Fact]
    public void TryParse_accepts_short_hex_parts()
    {
        Assert.True(AdapterLuid.TryParse("0x1_0x2", out var parsed));
        Assert.Equal(2u, parsed.LowPart);
        Assert.Equal(1, parsed.HighPart);
    }

    [Theory]
    [InlineData("luid")]
    [InlineData("0x1_2")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_rejects_other_text(string? text)
    {
        Assert.False(AdapterLuid.TryParse(text, out _));
    }
}
