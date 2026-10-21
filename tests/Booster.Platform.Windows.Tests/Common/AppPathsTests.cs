using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Tests.Common;

public sealed class AppPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Booster.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Folders_live_under_data_root_and_are_created_on_first_access()
    {
        var data = Path.Combine(_root, "data");
        var paths = new AppPaths(data, Path.Combine(_root, "app"));
        Assert.False(Directory.Exists(data));

        Assert.Equal(Path.Combine(data, "logs"), paths.Logs);
        Assert.True(Directory.Exists(paths.Logs));
        Assert.Equal(Path.Combine(data, "reports"), paths.Reports);
        Assert.Equal(Path.Combine(data, "captures"), paths.Captures);
        Assert.Equal(Path.Combine(data, "tools"), paths.Tools);
        Assert.Equal(Path.Combine(data, "config"), paths.UserConfig);
        Assert.True(Directory.Exists(paths.UserConfig));
        Assert.Equal(Path.Combine(data, "journal"), paths.Journal);
        Assert.True(Directory.Exists(paths.Journal));
        Assert.Equal(Path.Combine(data, "benchmarks"), paths.Benchmarks);
        Assert.True(Directory.Exists(paths.Benchmarks));
        Assert.Equal(Path.Combine(data, "traces"), paths.Traces);
        Assert.True(Directory.Exists(paths.Traces));
        Assert.Equal(Path.Combine(data, "state"), paths.State);
        Assert.True(Directory.Exists(paths.State));
    }

    [Fact]
    public void Built_in_config_is_next_to_exe_and_not_created()
    {
        var app = Path.Combine(_root, "app");
        var paths = new AppPaths(Path.Combine(_root, "data"), app);

        Assert.Equal(Path.Combine(app, "config"), paths.BuiltInConfig);
        Assert.False(Directory.Exists(paths.BuiltInConfig));
        Assert.Equal(Path.Combine(app, "config", "Profiles"), paths.BuiltInProfiles);
        Assert.False(Directory.Exists(paths.BuiltInProfiles));
    }

    [Fact]
    public void User_profiles_folder_is_not_created_on_access()
    {
        var data = Path.Combine(_root, "data");
        var paths = new AppPaths(data, Path.Combine(_root, "app"));

        Assert.Equal(Path.Combine(data, "config", "Profiles"), paths.UserProfiles);
        Assert.False(Directory.Exists(paths.UserProfiles));
    }

    [Fact]
    public void Base_directory_is_the_full_path_of_the_app_folder()
    {
        var paths = new AppPaths(Path.Combine(_root, "data"), Path.Combine(_root, "app", "..", "app"));

        Assert.Equal(Path.Combine(_root, "app"), paths.BaseDirectory);
        Assert.False(Directory.Exists(paths.BaseDirectory));
    }

    [Fact]
    public void Default_data_root_is_local_app_data_booster()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Booster");

        Assert.Equal(expected, AppPaths.DefaultDataRoot);
    }
}
