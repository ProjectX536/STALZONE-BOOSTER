using System.Text.Json;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Booster.Platform.Windows.Common;

/// <summary>
/// Серии в <see cref="IAppPaths.Benchmarks"/>, по файлу <c>&lt;Id&gt;.json</c> на серию; запись через <see cref="AtomicFile"/>.
/// Операции идут по одной: сохранение с удалением старых не пересекается с загрузкой и удалением.
/// </summary>
public sealed partial class BenchmarkFileStore : IBenchmarkStore
{
    private const string Extension = ".json";
    private const string TempSuffix = ".json.tmp";

    private readonly IAppPaths _paths;
    private readonly IBoosterConfiguration _config;
    private readonly ILogger<BenchmarkFileStore> _logger;
    private readonly Func<string, string, CancellationToken, Task> _atomicWrite;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BenchmarkFileStore(IAppPaths paths, IBoosterConfiguration config, ILogger<BenchmarkFileStore> logger)
        : this(paths, config, logger, AtomicFile.WriteAllTextAsync)
    {
    }

    /// <param name="atomicWrite">Шов для тестов: запись с заменой файла.</param>
    internal BenchmarkFileStore(IAppPaths paths, IBoosterConfiguration config, ILogger<BenchmarkFileStore> logger,
        Func<string, string, CancellationToken, Task> atomicWrite)
    {
        _paths = paths;
        _config = config;
        _logger = logger;
        _atomicWrite = atomicWrite;
    }

    public async Task<BenchmarkSeries> SaveAsync(BenchmarkSeries series, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(series);
        CheckId(series.Id);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var dir = _paths.Benchmarks;
            var id = series.Id;
            for (var suffix = 2; File.Exists(PathOf(dir, id)); suffix++)
                id = $"{series.Id}-{suffix}";
            var saved = series with { Id = id };

            await WriteAsync(PathOf(dir, id), BenchmarkJson.Serialize(saved), ct).ConfigureAwait(false);
            await PruneAsync(dir, ct).ConfigureAwait(false);
            return saved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<BenchmarkSeries>> LoadAllAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return (await LoadEntriesAsync(_paths.Benchmarks, ct).ConfigureAwait(false)).Select(entry => entry.Series).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        CheckId(id);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            File.Delete(PathOf(_paths.Benchmarks, id));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Лишние сверх <c>benchmark.retainedSeriesCount</c> — самые старые из читаемых. Испорченные файлы и файлы новой схемы
    /// не удаляются: их может прочитать более новая версия Booster или человек.
    /// </summary>
    private async Task PruneAsync(string dir, CancellationToken ct)
    {
        var retained = _config.Current.Settings.Benchmark.RetainedSeriesCount;
        foreach (var entry in (await LoadEntriesAsync(dir, ct).ConfigureAwait(false)).Skip(retained))
        {
            try
            {
                File.Delete(entry.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogPruneFailed(Path.GetFileName(entry.Path), ex);
            }
        }
    }

    /// <summary>Новые первыми: по времени старта, при равном — по Id. Id серии — имя файла, даже если файл переименовали.</summary>
    private async Task<IReadOnlyList<(string Path, BenchmarkSeries Series)>> LoadEntriesAsync(string dir, CancellationToken ct)
    {
        if (!Directory.Exists(dir))
            return [];

        RemoveLeftoverTempFiles(dir);
        var entries = new List<(string Path, BenchmarkSeries Series)>();
        foreach (var path in Directory.EnumerateFiles(dir, "*" + Extension)
                     .Where(f => string.Equals(Path.GetExtension(f), Extension, StringComparison.OrdinalIgnoreCase)))
        {
            var name = Path.GetFileName(path);
            try
            {
                var series = BenchmarkJson.Deserialize(await ReadAsync(path, ct).ConfigureAwait(false));
                entries.Add((path, series with { Id = Path.GetFileNameWithoutExtension(path) }));
            }
            catch (NotSupportedException ex)
            {
                LogSkippedNewerSchema(name, ex.Message);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                LogSkippedUnreadable(name, ex);
            }
        }

        return entries
            .OrderByDescending(entry => entry.Series.StartedAt)
            .ThenByDescending(entry => entry.Series.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static void RemoveLeftoverTempFiles(string dir)
    {
        // Маска с расширением в Windows захватывает и более длинные: фильтр по полному суффиксу.
        foreach (var temp in Directory.EnumerateFiles(dir, "*" + TempSuffix)
                     .Where(f => f.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
                // Занят записью, которая сейчас идёт: её собственная замена его и уберёт.
            }
        }
    }

    /// <summary>Чтение не мешает замене файла другим процессом: доступ на запись и удаление разделяется.</summary>
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

    /// <summary>Замена может упасть на нарушении совместного доступа (антивирус, индексатор держат файл): одна повторная попытка.</summary>
    private async Task WriteAsync(string path, string json, CancellationToken ct)
    {
        try
        {
            await _atomicWrite(path, json, ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            await _atomicWrite(path, json, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Id становится именем файла: путь в нём вывел бы запись и удаление за пределы каталога истории.</summary>
    private static void CheckId(string id) => FileNames.CheckBare(id);

    private static string PathOf(string dir, string id) => Path.Combine(dir, id + Extension);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Benchmark history: {File} is skipped, it is unreadable")]
    private partial void LogSkippedUnreadable(string file, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Benchmark history: {File} is skipped: {Detail}")]
    private partial void LogSkippedNewerSchema(string file, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Benchmark history: old series {File} was not removed")]
    private partial void LogPruneFailed(string file, Exception exception);
}
