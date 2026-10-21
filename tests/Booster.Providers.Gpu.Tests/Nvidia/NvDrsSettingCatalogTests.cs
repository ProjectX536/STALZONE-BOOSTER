using Booster.Core.Abstractions;
using Booster.Providers.Gpu.Nvidia;
using Booster.Providers.Gpu.Resources;

namespace Booster.Providers.Gpu.Tests.Nvidia;

public sealed class NvDrsSettingCatalogTests
{
    private const uint PowerMode = 0x1057EB71;
    private const uint FrameLimit = 0x10835002;
    private const uint VSync = 0x00A879CF;

    private static NvDrsValue Value(uint id, uint value, bool predefined = false) => new(id, value, predefined, 0);

    private static GpuSetting Describe(uint id, NvDrsValue? value) =>
        NvDrsSettingCatalog.Describe(NvDrsSettingCatalog.Entries.Single(e => e.SettingId == id), value);

    [Fact]
    public void Catalog_lists_the_six_settings_of_the_plan() =>
        Assert.Equal([0x1057EB71u, 0x10835002u, 0x007BA09Eu, 0x00A879CFu, 0x1194F158u, 0x00CE2691u], NvDrsSettingCatalog.SettingIds);

    [Fact]
    public void Known_values_get_names()
    {
        var setting = Describe(PowerMode, Value(PowerMode, 1));

        Assert.Equal("0x1057EB71", setting.Id);
        Assert.Equal("Режим управления электропитанием", setting.DisplayName);
        Assert.Equal(Reading<string>.Known("максимальная производительность", "nvapi"), setting.CurrentValue);
        Assert.Equal(GpuSettingOrigin.UserChanged, setting.Origin);
        Assert.True(setting.IsReadOnly);
        Assert.Equal("выключено", Describe(VSync, Value(VSync, 0x08416747)).CurrentValue.Value);
    }

    [Fact]
    public void Unknown_value_is_shown_as_hex() =>
        Assert.Equal("значение 0x0000002A", Describe(PowerMode, Value(PowerMode, 42)).CurrentValue.Value);

    [Fact]
    public void Missing_key_is_not_set()
    {
        var setting = Describe(PowerMode, null);

        Assert.Equal(GpuSettingOrigin.NotSet, setting.Origin);
        Assert.Equal(Reading<string>.Unknown("nvapi", ProviderStrings.NvDrsNotSet), setting.CurrentValue);
        Assert.True(setting.IsReadOnly);
    }

    [Fact]
    public void Predefined_value_is_driver_default() =>
        Assert.Equal(GpuSettingOrigin.DriverDefault, Describe(PowerMode, Value(PowerMode, 0, predefined: true)).Origin);

    [Fact]
    public void Frame_limit_shows_fps()
    {
        Assert.Equal("141 кадр/с", Describe(FrameLimit, Value(FrameLimit, 141)).CurrentValue.Value);
        Assert.Equal("выключено", Describe(FrameLimit, Value(FrameLimit, 0)).CurrentValue.Value);
    }
}
