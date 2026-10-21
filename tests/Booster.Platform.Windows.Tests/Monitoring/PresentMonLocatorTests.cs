using Booster.Core.Tests.TestSupport;
using Booster.Platform.Windows.Monitoring;
using Booster.Platform.Windows.Tests.TestSupport;

namespace Booster.Platform.Windows.Tests.Monitoring;

public class PresentMonLocatorTests
{
    private readonly FakeAppPaths _paths = new();
    private readonly FakeFileProbe _files = new();

    private string InTools(string fileName) => Path.Combine(_paths.Tools, "PresentMon", fileName);

    private PresentMonLocation? Locate(string? configuredPath = null) =>
        new PresentMonLocator(_files, new FakeConfiguration(presentMonPath: configuredPath), _paths).Locate();

    [Fact]
    public void Configured_path_that_exists_wins()
    {
        const string configured = @"D:\Portable\pm.exe";
        _files.Add(configured, version: "2.4.1.0").Add(InTools("PresentMon-2.6.0-x64.exe"));

        Assert.Equal(new PresentMonLocation(configured, "2.4.1.0"), Locate(configured));
    }

    [Fact]
    public void Missing_configured_path_falls_back_to_newest_in_tools()
    {
        _files.Add(InTools("PresentMon-2.5.0-x64.exe")).Add(InTools("PresentMon-2.6.0-x64.exe"));

        Assert.Equal(new PresentMonLocation(InTools("PresentMon-2.6.0-x64.exe"), "2.6.0"), Locate(@"D:\Missing\pm.exe"));
    }

    [Fact]
    public void Versions_compare_as_numbers_not_text()
    {
        _files.Add(InTools("PresentMon-2.10.0-x64.exe")).Add(InTools("PresentMon-2.9.0-x64.exe"));

        Assert.Equal(InTools("PresentMon-2.10.0-x64.exe"), Locate()!.ExecutablePath);
    }

    [Fact]
    public void Version_resource_is_preferred_over_file_name()
    {
        _files.Add(InTools("PresentMon-2.6.0-x64.exe"), version: "2.6.0.123");

        Assert.Equal("2.6.0.123", Locate()!.Version);
    }

    [Fact]
    public void Files_without_a_version_in_the_name_are_ignored()
    {
        _files.Add(InTools("PresentMon-beta-x64.exe")).Add(InTools("PresentMon-2.6-x64.exe"));

        Assert.Null(Locate());
    }

    [Fact]
    public void Folder_next_to_the_app_is_searched_last()
    {
        var nextToApp = Path.Combine(_paths.BaseDirectory, "PresentMon-2.7.0-x64.exe");
        _files.Add(nextToApp);
        Assert.Equal(nextToApp, Locate()!.ExecutablePath);

        _files.Add(InTools("PresentMon-2.6.0-x64.exe"));
        Assert.Equal(InTools("PresentMon-2.6.0-x64.exe"), Locate()!.ExecutablePath);
    }

    [Fact]
    public void Nothing_found_is_null() => Assert.Null(Locate(@"D:\Missing\pm.exe"));
}
