using Booster.Core.Abstractions;
using Booster.Core.GameNetwork;
using Microsoft.Extensions.Logging;

namespace Booster.Platform.Windows.Common;

public sealed partial class GameNetworkFileStore(IAppPaths paths, ILogger<GameNetworkFileStore> logger) : IGameNetworkStore
{
    private const string Extension = ".json";
    private const string TempSuffix = ".json.tmp";

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task SaveAsync(GameNetworkMeasurement measurement, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        FileNames.CheckBare(measurement.Id);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = PathOf(paths.NetworkMeasurements, measurement.Id);
            var json = GameNetworkJson.Serialize(measurement);
            try
            {
                await AtomicFile.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
            }
            catch (IOException)
            {
                await AtomicFile.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<GameNetworkMeasurement>> LoadAllAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var dir = paths.NetworkMeasurements;
            var loaded = new List<GameNetworkMeasurement>();
            RemoveLeftoverTempFiles(dir);
            foreach (var path in Directory.EnumerateFiles(dir, "*" + Extension)
                         .Where(f => string.Equals(Path.GetExtension(f), Extension, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var measurement = GameNetworkJson.Deserialize(await ReadAsync(path, ct).ConfigureAwait(false));
                    if (measurement is null)
                        LogSkippedInvalid(Path.GetFileName(path));
                    else
                        loaded.Add(measurement with { Id = Path.GetFileNameWithoutExtension(path) });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LogSkippedUnreadable(Path.GetFileName(path), ex);
                }
            }

            return loaded
                .OrderByDescending(m => m.StartedAt)
                .ThenByDescending(m => m.Id, StringComparer.Ordinal)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        FileNames.CheckBare(id);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            File.Delete(PathOf(paths.NetworkMeasurements, id));
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void RemoveLeftoverTempFiles(string dir)
    {
        // Маска *.ext в Windows находит и более длинные расширения.
        foreach (var temp in Directory.EnumerateFiles(dir, "*" + TempSuffix)
                     .Where(f => f.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
                // Занят другой записью, она его и уберёт.
            }
        }
    }

    private static async Task<string> ReadAsync(string path, CancellationToken ct)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096,
            FileOptions.Asynchronous);
        await using (stream.ConfigureAwait(false))
        {
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }
    }

    private static string PathOf(string dir, string id) => Path.Combine(dir, id + Extension);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Game network history: {File} is skipped, it is unreadable")]
    private partial void LogSkippedUnreadable(string file, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Game network history: {File} is skipped, it is damaged or written by a newer version")]
    private partial void LogSkippedInvalid(string file);
}
