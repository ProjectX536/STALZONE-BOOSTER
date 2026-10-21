using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Tests.Common;

public sealed class ProfileFileSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Booster.Tests", Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly ProfileFileSource _source;

    public ProfileFileSourceTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "data"), Path.Combine(_root, "app"));
        _source = new ProfileFileSource(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Built_in_profiles_are_listed_in_ascending_order_and_only_json()
    {
        Directory.CreateDirectory(_paths.BuiltInProfiles);
        foreach (var name in new[] { "Zeta.json", "Competitive.json", "notes.txt" })
            File.WriteAllText(Path.Combine(_paths.BuiltInProfiles, name), "{}");

        Assert.Equal(["Competitive.json", "Zeta.json"], _source.ListBuiltIn());
        Assert.Equal("{}", _source.ReadBuiltIn("Competitive.json"));
    }

    [Fact]
    public void Missing_built_in_folder_lists_nothing()
    {
        Assert.Empty(_source.ListBuiltIn());
    }

    [Fact]
    public void Missing_user_file_reads_as_null_without_creating_folders()
    {
        Assert.Null(_source.ReadUser("Competitive.json"));
        Assert.False(Directory.Exists(_paths.UserProfiles));
    }

    [Fact]
    public async Task Writing_creates_the_folder_and_the_file()
    {
        await _source.WriteUserAsync("Competitive.json", "{\"games\":[\"тест\"]}", TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(_paths.UserProfiles, "Competitive.json")));
        Assert.Equal("{\"games\":[\"тест\"]}", _source.ReadUser("Competitive.json"));
        Assert.False(File.Exists(Path.Combine(_paths.UserProfiles, "Competitive.json.tmp")));
    }

    [Fact]
    public void Leftover_temp_file_is_removed_on_read()
    {
        Directory.CreateDirectory(_paths.UserProfiles);
        File.WriteAllText(Path.Combine(_paths.UserProfiles, "Competitive.json"), "old");
        File.WriteAllText(Path.Combine(_paths.UserProfiles, "Competitive.json.tmp"), "half");

        Assert.Equal("old", _source.ReadUser("Competitive.json"));
        Assert.False(File.Exists(Path.Combine(_paths.UserProfiles, "Competitive.json.tmp")));
    }

    [Theory]
    [InlineData(@"..\Competitive.json")]
    [InlineData(@"sub\Competitive.json")]
    [InlineData(@"C:\Competitive.json")]
    [InlineData("")]
    public async Task File_name_with_path_is_rejected(string fileName)
    {
        Assert.ThrowsAny<ArgumentException>(() => _source.ReadUser(fileName));
        Assert.ThrowsAny<ArgumentException>(() => _source.ReadBuiltIn(fileName));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _source.WriteUserAsync(fileName, "{}", TestContext.Current.CancellationToken));
    }
}
