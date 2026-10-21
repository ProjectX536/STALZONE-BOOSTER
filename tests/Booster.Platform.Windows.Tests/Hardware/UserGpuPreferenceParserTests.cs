using Booster.Core.Inventory;
using Booster.Platform.Windows.Hardware;

namespace Booster.Platform.Windows.Tests.Hardware;

public class UserGpuPreferenceParserTests
{
    private const string Game = @"C:\Games\Тестовая игра\Game.exe";

    private static IReadOnlyList<AppGpuPreference> Parse(params (string Name, object? Value)[] values) =>
        UserGpuPreferenceParser.Parse(values.Select(v => KeyValuePair.Create(v.Name, v.Value)).ToList());

    [Theory]
    [InlineData("GpuPreference=2;", AppGpuPreferenceKind.HighPerformance)]
    [InlineData("SwapEffectUpgradeEnable=1;GpuPreference=1;", AppGpuPreferenceKind.PowerSaving)]
    [InlineData("GpuPreference=0;", AppGpuPreferenceKind.SystemDefault)]
    [InlineData(" gpupreference = 2 ", AppGpuPreferenceKind.HighPerformance)]
    public void Preference_is_read_from_value(string value, AppGpuPreferenceKind expected)
    {
        var preference = Assert.Single(Parse((Game, value)));

        Assert.Equal(expected, preference.Preference);
    }

    [Fact]
    public void Executable_path_is_kept_as_is()
    {
        Assert.Equal(Game, Assert.Single(Parse((Game, "GpuPreference=2;"))).ExecutablePath);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("GpuPreference=9;")]
    [InlineData("GpuPreference=;")]
    [InlineData("SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;")]
    [InlineData("")]
    public void Value_without_valid_preference_is_skipped(string value) =>
        Assert.Empty(Parse((Game, value)));

    [Fact]
    public void Non_string_value_is_skipped() =>
        Assert.Empty(Parse((Game, 2), (@"C:\b.exe", null), (@"C:\c.exe", new[] { "GpuPreference=2;" })));

    [Fact]
    public void Several_apps_are_returned_in_registry_order()
    {
        var preferences = Parse(
            ("DirectXUserGlobalSettings", "SwapEffectUpgradeEnable=1;"),
            (@"C:\a.exe", "GpuPreference=1;"),
            (@"C:\b.exe", "GpuPreference=2;"));

        Assert.Equal([@"C:\a.exe", @"C:\b.exe"], preferences.Select(p => p.ExecutablePath));
    }
}
