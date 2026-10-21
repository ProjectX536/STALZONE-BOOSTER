using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Hardware;

namespace Booster.Platform.Windows.Tests.Hardware;

public class DisplayMapperTests
{
    private static readonly AdapterLuid Nvidia = new(0x0000D2E3, 0);

    private static DisplayPathRecord Path(uint numerator = 144000, uint denominator = 1000, string? name = "MSI G27C4",
        bool? hdrSupported = false, bool? hdrEnabled = false, bool primary = true) =>
        new(name, 1920, 1080, numerator, denominator, Nvidia, hdrSupported, hdrEnabled, primary);

    private static DisplayInfo Single(DisplayPathRecord path)
    {
        var displays = DisplayMapper.Map([path]);
        Assert.True(displays.IsAvailable);
        return Assert.Single(displays.Value!);
    }

    [Theory]
    [InlineData(144000u, 1000u, 144.0)]
    [InlineData(59940u, 1000u, 59.94)]
    [InlineData(143856u, 1000u, 143.856)]
    [InlineData(60000u, 1001u, 59.94)]
    public void Refresh_rate_is_numerator_over_denominator(uint numerator, uint denominator, double expected) =>
        Assert.Equal(expected, Single(Path(numerator, denominator)).RefreshRateHz.Value);

    [Theory]
    [InlineData(144000u, 0u)]
    [InlineData(0u, 1000u)]
    public void Zero_refresh_part_is_unknown(uint numerator, uint denominator) =>
        Assert.Equal(DataStatus.Unknown, Single(Path(numerator, denominator)).RefreshRateHz.Status);

    [Fact]
    public void Missing_advanced_color_info_is_unknown()
    {
        var display = Single(Path(hdrSupported: null, hdrEnabled: null));

        Assert.Equal(DataStatus.Unknown, display.HdrSupported.Status);
        Assert.Equal(DataStatus.Unknown, display.HdrEnabled.Status);
    }

    [Fact]
    public void Supported_but_disabled_hdr_is_reported()
    {
        var display = Single(Path(hdrSupported: true, hdrEnabled: false));

        Assert.True(display.HdrSupported.Value);
        Assert.False(display.HdrEnabled.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_monitor_name_is_unknown(string? name) =>
        Assert.Equal(DataStatus.Unknown, Single(Path(name: name)).MonitorName.Status);

    [Fact]
    public void Resolution_adapter_and_primary_flag_are_kept()
    {
        var display = Single(Path());

        Assert.Equal("MSI G27C4", display.MonitorName.Value);
        Assert.Equal(1920, display.Width.Value);
        Assert.Equal(1080, display.Height.Value);
        Assert.Equal(Nvidia, display.AdapterLuid.Value);
        Assert.True(display.IsPrimary);
    }

    [Fact]
    public void Vrr_is_always_unknown()
    {
        var display = Single(Path());

        Assert.Equal(DataStatus.Unknown, display.VrrEnabled.Status);
        Assert.NotNull(display.VrrEnabled.Note);
    }

    [Fact]
    public void No_active_paths_is_unknown_with_note()
    {
        var displays = DisplayMapper.Map([]);

        Assert.Equal(DataStatus.Unknown, displays.Status);
        Assert.NotNull(displays.Note);
    }

    [Fact]
    public void Zero_resolution_is_unknown()
    {
        var display = Single(Path() with { Width = 0, Height = 0 });

        Assert.Equal(DataStatus.Unknown, display.Width.Status);
        Assert.Equal(DataStatus.Unknown, display.Height.Status);
    }
}
