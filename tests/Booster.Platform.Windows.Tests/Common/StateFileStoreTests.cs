using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Tests.Common;

public sealed class StateFileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Booster.Tests", Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;

    public StateFileStoreTests() => _paths = new AppPaths(Path.Combine(_root, "data"), Path.Combine(_root, "app"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Missing_file_reads_null()
    {
        Assert.Null(await new StateFileStore(_paths).ReadAsync("absent.json", Ct));
    }

    [Fact]
    public async Task Write_then_read_round_trips()
    {
        var store = new StateFileStore(_paths);

        await store.WriteAsync("a.json", "{\"v\":1}", Ct);
        await store.WriteAsync("a.json", "{\"v\":\"тест\"}", Ct);

        Assert.Equal("{\"v\":\"тест\"}", await store.ReadAsync("a.json", Ct));
        Assert.Equal(Path.Combine(_root, "data", "state", "a.json"), Directory.GetFiles(_paths.State).Single());

        store.Delete("a.json");
        store.Delete("a.json");
        Assert.Null(await store.ReadAsync("a.json", Ct));
    }

    [Theory]
    [InlineData(@"..\a.json")]
    [InlineData(@"sub\a.json")]
    [InlineData(@"C:\a.json")]
    public async Task Name_with_path_is_rejected(string name)
    {
        var store = new StateFileStore(_paths);

        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadAsync(name, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => store.WriteAsync(name, "{}", Ct));
        Assert.Throws<ArgumentException>(() => store.Delete(name));
    }
}
