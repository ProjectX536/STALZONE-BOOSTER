using Booster.Core.Abstractions;
using Booster.Core.GameNetwork;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;
using Booster.Platform.Windows.Common;
using Microsoft.Extensions.Logging;

namespace Booster.Platform.Windows.Tests.Common;

public sealed class GameNetworkFileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Booster.Tests", Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly ListLogger<GameNetworkFileStore> _logger = new();

    public GameNetworkFileStoreTests() => _paths = new AppPaths(Path.Combine(_root, "data"), Path.Combine(_root, "app"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static GameNetworkMeasurement Sample(string id, int minutes = 0) => new(GameNetworkMeasurement.CurrentSchemaVersion, id,
        new DateTimeOffset(2026, 10, 21, 12, minutes, 0, TimeSpan.Zero), TimeSpan.FromSeconds(1), "game", "label",
        new MeasuredServer("203.0.113.5", ServerSource.Manual, []), null, false,
        [new TargetSeries(NetworkTargetKind.Host, "203.0.113.5", [10, null, 12])], null, Reading<WifiStatus>.Unknown("fake"),
        Reading<IReadOnlyList<ConsumerTraffic>>.Unknown("fake"), [], Reading<double>.Known(1.5, "fake"), MeasurementEnd.Completed);

    [Fact]
    public async Task Saved_measurement_loads_back()
    {
        var store = new GameNetworkFileStore(_paths, _logger);

        await store.SaveAsync(Sample("a", 0), Ct);
        await store.SaveAsync(Sample("b", 5), Ct);
        var loaded = await store.LoadAllAsync(Ct);

        Assert.Equal(["b", "a"], loaded.Select(m => m.Id));
        Assert.Equal(GameNetworkJson.Serialize(Sample("a", 0)), GameNetworkJson.Serialize(loaded[1]));
        Assert.Equal(["a.json", "b.json"], Directory.GetFiles(_paths.NetworkMeasurements).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task Broken_file_is_skipped()
    {
        var store = new GameNetworkFileStore(_paths, _logger);
        File.WriteAllText(Path.Combine(_paths.NetworkMeasurements, "broken.json"), "{");
        var future = GameNetworkJson.Serialize(Sample("future")).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(_paths.NetworkMeasurements, "future.json"), future);
        await store.SaveAsync(Sample("a"), Ct);

        var loaded = await store.LoadAllAsync(Ct);

        Assert.Equal("a", Assert.Single(loaded).Id);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("broken.json", StringComparison.Ordinal));
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("future.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Delete_removes_file()
    {
        var store = new GameNetworkFileStore(_paths, _logger);
        await store.SaveAsync(Sample("a"), Ct);
        await store.SaveAsync(Sample("b", 5), Ct);

        await store.DeleteAsync("a", Ct);
        await store.DeleteAsync("missing", Ct);

        Assert.Equal(["b"], (await store.LoadAllAsync(Ct)).Select(m => m.Id));
    }

    [Theory]
    [InlineData(@"..\x")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData(" ")]
    public async Task Id_with_path_separator_is_rejected(string id)
    {
        var store = new GameNetworkFileStore(_paths, _logger);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.DeleteAsync(id, Ct));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.SaveAsync(Sample(id), Ct));
        Assert.False(Directory.Exists(_paths.DataRoot));
    }
}
