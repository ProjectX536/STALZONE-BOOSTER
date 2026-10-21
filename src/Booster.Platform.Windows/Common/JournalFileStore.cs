using Booster.Core.Abstractions;

namespace Booster.Platform.Windows.Common;

/// <summary>Файлы журнала в <see cref="IAppPaths.Journal"/>; запись через <see cref="AtomicFile"/>.</summary>
public sealed class JournalFileStore : IJournalFileStore
{
    private const string Extension = ".json";
    private const string TempSuffix = ".json.tmp";

    private readonly IAppPaths _paths;
    private readonly Func<string, string, CancellationToken, Task> _atomicWrite;

    public JournalFileStore(IAppPaths paths) : this(paths, AtomicFile.WriteAllTextAsync)
    {
    }

    /// <param name="atomicWrite">Шов для тестов: запись с заменой файла.</param>
    internal JournalFileStore(IAppPaths paths, Func<string, string, CancellationToken, Task> atomicWrite)
    {
        _paths = paths;
        _atomicWrite = atomicWrite;
    }

    public IReadOnlyList<string> List()
    {
        var dir = _paths.Journal;
        if (!Directory.Exists(dir))
            return [];

        // Маска с трёхбуквенным расширением в Windows захватывает и более длинные: фильтр по полному суффиксу.
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

        return Directory.EnumerateFiles(dir)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => string.Equals(Path.GetExtension(name), Extension, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Чтение не мешает замене файла другим процессом или записью Booster: доступ на запись и удаление разделяется.</summary>
    public async Task<string> ReadAsync(string fileName, CancellationToken ct)
    {
        var stream = new FileStream(PathOf(fileName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            4096, FileOptions.Asynchronous);
        await using (stream.ConfigureAwait(false))
        {
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Замена может упасть на нарушении совместного доступа (антивирус, индексатор держат файл): одна повторная попытка.</summary>
    public async Task WriteAsync(string fileName, string json, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(json);
        var path = PathOf(fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            await _atomicWrite(path, json, ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            await _atomicWrite(path, json, ct).ConfigureAwait(false);
        }
    }

    public void Delete(string fileName) => File.Delete(PathOf(fileName));

    private string PathOf(string fileName) => Path.Combine(_paths.Journal, FileNames.CheckBare(fileName));
}
