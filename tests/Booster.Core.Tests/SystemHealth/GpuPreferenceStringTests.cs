using Booster.Core.SystemHealth;

namespace Booster.Core.Tests.SystemHealth;

public class GpuPreferenceStringTests
{
    [Theory]
    [InlineData(null, "GpuPreference=2;")]
    [InlineData("", "GpuPreference=2;")]
    [InlineData("SwapEffectUpgradeEnable=1;", "SwapEffectUpgradeEnable=1;GpuPreference=2;")]
    [InlineData("SwapEffectUpgradeEnable=1", "SwapEffectUpgradeEnable=1;GpuPreference=2;")]
    public void Set_appends_with_semicolon(string? current, string expected)
    {
        Assert.Equal(expected, GpuPreferenceString.Set(current, "GpuPreference=2"));
    }

    [Fact]
    public void Set_replaces_existing_key_and_keeps_others()
    {
        var updated = GpuPreferenceString.Set("AutoHDREnable=1;GpuPreference=1;SwapEffectUpgradeEnable=0;", "GpuPreference=2");

        Assert.Equal("AutoHDREnable=1;GpuPreference=2;SwapEffectUpgradeEnable=0;", updated);
    }

    [Theory]
    [InlineData("GpuPreference=2;", true)]
    [InlineData("AutoHDREnable=1;GpuPreference=2", true)]
    [InlineData("GpuPreference=1;", false)]
    [InlineData("XGpuPreference=2;", false)]
    [InlineData("GpuPreference=20;", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Contains_matches_key_and_value(string? current, bool expected)
    {
        Assert.Equal(expected, GpuPreferenceString.Contains(current, "GpuPreference=2"));
    }
}
