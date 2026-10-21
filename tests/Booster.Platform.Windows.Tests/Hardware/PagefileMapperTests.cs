using Booster.Core.Abstractions;
using Booster.Platform.Windows.Hardware;
using static Booster.Platform.Windows.Tests.TestSupport.WmiFixture;

namespace Booster.Platform.Windows.Tests.Hardware;

public class PagefileMapperTests
{
    [Fact]
    public void Automatic_management_is_read()
    {
        var configuration = PagefileMapper.Map(Row(("AutomaticManagedPagefile", true)), [], []);

        Assert.Equal(DataStatus.Available, configuration.AutomaticManaged.Status);
        Assert.True(configuration.AutomaticManaged.Value);
    }

    [Fact]
    public void Settings_and_usage_are_mapped()
    {
        var configuration = PagefileMapper.Map(Row(("AutomaticManagedPagefile", false)),
            [Row(("Name", @"C:\pagefile.sys"), ("InitialSize", 4096u), ("MaximumSize", 8192u))],
            [Row(("Name", @"C:\pagefile.sys"), ("AllocatedBaseSize", 4096u), ("CurrentUsage", 512u), ("PeakUsage", 1024u))]);

        Assert.False(configuration.AutomaticManaged.Value);
        Assert.Equal([new PagefileSetting(@"C:\pagefile.sys", 4096, 8192)], configuration.Settings);
        Assert.Equal([new PagefileUsage(@"C:\pagefile.sys", 4096, 512, 1024)], configuration.Usage);
    }

    [Fact]
    public void Missing_automatic_flag_is_unknown()
    {
        var withoutProperty = PagefileMapper.Map(Row(("Name", "PC")), [], []);
        var withoutRow = PagefileMapper.Map(null, [], []);

        Assert.Equal(DataStatus.Unknown, withoutProperty.AutomaticManaged.Status);
        Assert.Equal(DataStatus.Unknown, withoutRow.AutomaticManaged.Status);
    }

    [Fact]
    public void Rows_without_name_are_skipped()
    {
        var configuration = PagefileMapper.Map(null,
            [Row(("InitialSize", 4096u), ("MaximumSize", 8192u))],
            [Row(("Name", " "), ("AllocatedBaseSize", 4096u))]);

        Assert.Empty(configuration.Settings);
        Assert.Empty(configuration.Usage);
    }
}
