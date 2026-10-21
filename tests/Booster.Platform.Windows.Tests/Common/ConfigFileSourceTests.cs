using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Tests.Common;

public sealed class ConfigFileSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Booster.Tests", Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly ConfigFileSource _source;

    public ConfigFileSourceTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "data"), Path.Combine(_root, "app"));
        Directory.CreateDirectory(_paths.BuiltInConfig);
        _source = new ConfigFileSource(_paths);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string UserFile(string name) => Path.Combine(_paths.UserConfig, name);

    [Fact]
    public void Built_in_file_is_read_from_folder_next_to_exe()
    {
        File.WriteAllText(Path.Combine(_paths.BuiltInConfig, "settings.json"), "{\"schemaVersion\":1}");

        Assert.Equal("{\"schemaVersion\":1}", _source.ReadBuiltIn("settings.json"));
    }

    [Fact]
    public void Missing_user_file_reads_as_null()
    {
        Assert.Null(_source.ReadUser("settings.json"));
    }

    [Fact]
    public async Task Written_user_file_is_read_back_and_no_temp_file_remains()
    {
        await _source.WriteUserAsync("settings.json", "{\"a\":\"тест\"}", TestContext.Current.CancellationToken);

        Assert.Equal("{\"a\":\"тест\"}", _source.ReadUser("settings.json"));
        Assert.False(File.Exists(UserFile("settings.json.tmp")));
    }

    [Fact]
    public async Task Writing_again_replaces_the_file()
    {
        await _source.WriteUserAsync("settings.json", "old", TestContext.Current.CancellationToken);
        await _source.WriteUserAsync("settings.json", "new", TestContext.Current.CancellationToken);

        Assert.Equal("new", _source.ReadUser("settings.json"));
    }

    [Fact]
    public void Leftover_temp_file_is_ignored_and_removed()
    {
        // Сбой между записью .tmp и переименованием: основной файл целиком старый.
        File.WriteAllText(UserFile("settings.json"), "old");
        File.WriteAllText(UserFile("settings.json.tmp"), "half-writ");

        Assert.Equal("old", _source.ReadUser("settings.json"));
        Assert.False(File.Exists(UserFile("settings.json.tmp")));
    }

    [Theory]
    [InlineData(@"..\settings.json")]
    [InlineData(@"sub\settings.json")]
    [InlineData(@"C:\settings.json")]
    [InlineData("")]
    public void File_name_with_path_is_rejected(string fileName)
    {
        Assert.ThrowsAny<ArgumentException>(() => _source.ReadUser(fileName));
    }
}
