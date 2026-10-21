using Booster.Platform.Windows.Common;
using Booster.Platform.Windows.Tests.TestSupport;

namespace Booster.Platform.Windows.Tests.Common;

public sealed class JournalFileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Booster.Tests", Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;

    public JournalFileStoreTests() => _paths = new AppPaths(Path.Combine(_root, "data"), Path.Combine(_root, "app"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Writes_reads_and_replaces_a_file()
    {
        var store = new JournalFileStore(_paths);

        await store.WriteAsync("a.json", "{\"v\":1}", TestContext.Current.CancellationToken);
        await store.WriteAsync("a.json", "{\"v\":\"тест\"}", TestContext.Current.CancellationToken);

        Assert.Equal("{\"v\":\"тест\"}", await store.ReadAsync("a.json", TestContext.Current.CancellationToken));
        Assert.Equal(Path.Combine(_root, "data", "journal", "a.json"), Directory.GetFiles(_paths.Journal).Single());
    }

    [Fact]
    public void List_returns_json_files_and_removes_leftover_temp_files()
    {
        var dir = _paths.Journal;
        File.WriteAllText(Path.Combine(dir, "b.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "a.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "a.json.tmp"), "{");
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");
        File.WriteAllText(Path.Combine(dir, "c.jsonx"), "x");

        var names = new JournalFileStore(_paths).List();

        Assert.Equal(["a.json", "b.json"], names);
        Assert.False(File.Exists(Path.Combine(dir, "a.json.tmp")));
        Assert.True(File.Exists(Path.Combine(dir, "notes.txt")));
    }

    [Fact]
    public void Delete_removes_the_file()
    {
        File.WriteAllText(Path.Combine(_paths.Journal, "a.json"), "{}");

        new JournalFileStore(_paths).Delete("a.json");

        Assert.Empty(Directory.GetFiles(_paths.Journal));
    }

    [Theory]
    [InlineData(@"..\a.json")]
    [InlineData(@"a\b.json")]
    [InlineData("")]
    public async Task Names_with_paths_are_rejected(string name)
    {
        var store = new JournalFileStore(_paths);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.WriteAsync(name, "{}", TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.ReadAsync(name, TestContext.Current.CancellationToken));
        Assert.ThrowsAny<ArgumentException>(() => store.Delete(name));
    }

    [Fact]
    public async Task Read_succeeds_while_another_handle_holds_the_file_open_for_write()
    {
        var path = Path.Combine(_paths.Journal, "a.json");
        File.WriteAllText(path, "{}");
        await using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        Assert.Equal("{}", await new JournalFileStore(_paths).ReadAsync("a.json", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Write_retries_the_replace_once()
    {
        var attempts = 0;
        var store = new JournalFileStore(_paths, async (path, json, ct) =>
        {
            if (++attempts == 1)
                throw new IOException("sharing violation");
            await File.WriteAllTextAsync(path, json, ct);
        });

        await store.WriteAsync("a.json", "{}", TestContext.Current.CancellationToken);

        Assert.Equal(2, attempts);
        Assert.Equal("{}", File.ReadAllText(Path.Combine(_paths.Journal, "a.json")));
    }

    [Fact]
    public async Task Write_fails_when_the_retry_fails_too()
    {
        var attempts = 0;
        var store = new JournalFileStore(_paths, (_, _, _) =>
        {
            attempts++;
            throw new IOException("sharing violation");
        });

        await Assert.ThrowsAsync<IOException>(() => store.WriteAsync("a.json", "{}", TestContext.Current.CancellationToken));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void Missing_folder_lists_nothing()
    {
        var paths = new FakeAppPaths(_root);

        Assert.Empty(new JournalFileStore(paths).List());
        Assert.False(Directory.Exists(paths.Journal));
    }
}
