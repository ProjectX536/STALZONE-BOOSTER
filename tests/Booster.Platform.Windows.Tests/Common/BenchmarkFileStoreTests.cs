using Booster.Core.Benchmark;
using Booster.Core.Tests.TestSupport;
using Booster.Platform.Windows.Common;
using Booster.Platform.Windows.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace Booster.Platform.Windows.Tests.Common;

public sealed class BenchmarkFileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Booster.Tests", Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly ListLogger<BenchmarkFileStore> _logger = new();

    public BenchmarkFileStoreTests() => _paths = new AppPaths(Path.Combine(_root, "data"), Path.Combine(_root, "app"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private BenchmarkFileStore Create(int retained = 100, IAppPaths? paths = null) =>
        new(paths ?? _paths, new FakeConfiguration(benchmark: FakeConfiguration.DefaultBenchmark with { RetainedSeriesCount = retained }),
            _logger);

    private string[] FileNames() => Directory.GetFiles(_paths.Benchmarks).Select(Path.GetFileName).OfType<string>().Order().ToArray();

    [Fact]
    public async Task Save_then_load_returns_newest_first()
    {
        var store = Create();

        await store.SaveAsync(SampleSeries.Full("a", minutes: 0), Ct);
        await store.SaveAsync(SampleSeries.Full("c", minutes: 10), Ct);
        await store.SaveAsync(SampleSeries.Full("b", minutes: 5), Ct);

        var loaded = await store.LoadAllAsync(Ct);

        Assert.Equal(["c", "b", "a"], loaded.Select(s => s.Id));
        Assert.Equal(["a.json", "b.json", "c.json"], FileNames());
        Assert.Equal(BenchmarkJson.Serialize(SampleSeries.Full("a")), BenchmarkJson.Serialize(loaded[2]));
    }

    [Fact]
    public async Task Retention_keeps_newest_series()
    {
        var store = Create(retained: 2);

        await store.SaveAsync(SampleSeries.Full("a", minutes: 0), Ct);
        await store.SaveAsync(SampleSeries.Full("c", minutes: 10), Ct);
        await store.SaveAsync(SampleSeries.Full("b", minutes: 5), Ct);

        Assert.Equal(["b.json", "c.json"], FileNames());
        Assert.Equal(["c", "b"], (await store.LoadAllAsync(Ct)).Select(s => s.Id));
    }

    [Fact]
    public async Task Corrupted_and_future_files_are_skipped()
    {
        var store = Create(retained: 1);
        var dir = _paths.Benchmarks;
        File.WriteAllText(Path.Combine(dir, "broken.json"), "{");
        var future = BenchmarkJson.Serialize(SampleSeries.Full("future", minutes: 30))
            .Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(dir, "future.json"), future);

        await store.SaveAsync(SampleSeries.Full("a"), Ct);
        var loaded = await store.LoadAllAsync(Ct);

        Assert.Equal("a", Assert.Single(loaded).Id);
        Assert.Equal(["a.json", "broken.json", "future.json"], FileNames());
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("broken.json", StringComparison.Ordinal));
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("future.json", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(@"..\x")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData(" ")]
    public async Task Delete_rejects_path_in_id(string id)
    {
        var store = Create();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.DeleteAsync(id, Ct));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.SaveAsync(SampleSeries.Full(id), Ct));
        Assert.False(Directory.Exists(_paths.DataRoot));
    }

    [Fact]
    public async Task Delete_removes_only_that_series()
    {
        var store = Create();
        await store.SaveAsync(SampleSeries.Full("a", minutes: 0), Ct);
        await store.SaveAsync(SampleSeries.Full("b", minutes: 5), Ct);

        await store.DeleteAsync("a", Ct);
        await store.DeleteAsync("missing", Ct);

        Assert.Equal(["b"], (await store.LoadAllAsync(Ct)).Select(s => s.Id));
    }

    [Fact]
    public async Task Same_second_id_gets_unique_file_name()
    {
        var store = Create();

        var first = await store.SaveAsync(SampleSeries.Full("20261020-120000-game", minutes: 0), Ct);
        var second = await store.SaveAsync(SampleSeries.Full("20261020-120000-game", minutes: 1), Ct);

        Assert.Equal("20261020-120000-game", first.Id);
        Assert.Equal("20261020-120000-game-2", second.Id);
        Assert.Equal(["20261020-120000-game-2.json", "20261020-120000-game.json"], FileNames());
        Assert.Equal(["20261020-120000-game-2", "20261020-120000-game"], (await store.LoadAllAsync(Ct)).Select(s => s.Id));

        await store.DeleteAsync(second.Id, Ct);

        Assert.Equal(["20261020-120000-game.json"], FileNames());
    }

    [Fact]
    public async Task Loaded_id_is_the_file_name()
    {
        File.WriteAllText(Path.Combine(_paths.Benchmarks, "renamed.json"), BenchmarkJson.Serialize(SampleSeries.Full("original")));
        var store = Create();

        var loaded = Assert.Single(await store.LoadAllAsync(Ct));
        await store.DeleteAsync(loaded.Id, Ct);

        Assert.Equal("renamed", loaded.Id);
        Assert.Empty(FileNames());
    }

    [Fact]
    public async Task Load_ignores_other_files_and_removes_leftover_temp_files()
    {
        var dir = _paths.Benchmarks;
        File.WriteAllText(Path.Combine(dir, "a.json"), BenchmarkJson.Serialize(SampleSeries.Full("a")));
        File.WriteAllText(Path.Combine(dir, "a.json.tmp"), "{");
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");
        File.WriteAllText(Path.Combine(dir, "b.jsonx"), "x");

        var loaded = await Create().LoadAllAsync(Ct);

        Assert.Equal("a", Assert.Single(loaded).Id);
        Assert.Equal(["a.json", "b.jsonx", "notes.txt"], FileNames());
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task Missing_folder_loads_nothing()
    {
        var paths = new FakeAppPaths(_root);

        Assert.Empty(await Create(paths: paths).LoadAllAsync(Ct));
        Assert.False(Directory.Exists(paths.Benchmarks));
    }

    [Fact]
    public async Task Save_retries_the_replace_once()
    {
        var attempts = 0;
        var store = new BenchmarkFileStore(_paths, new FakeConfiguration(), _logger, async (path, json, ct) =>
        {
            if (++attempts == 1)
                throw new IOException("sharing violation");
            await File.WriteAllTextAsync(path, json, ct);
        });

        await store.SaveAsync(SampleSeries.Full("a"), Ct);

        Assert.Equal(2, attempts);
        Assert.Equal("a", Assert.Single(await store.LoadAllAsync(Ct)).Id);
    }
}
