using Booster.Core.Abstractions;

namespace Booster.Platform.Windows.Common;

public sealed class JournalFileStore : IJournalFileStore
{
    private const string Extension = ".json";
    private const string TempSuffix = ".json.tmp";

    private readonly IAppPaths _paths;
    private readonly Func<string, string, CancellationToken, Task> _atomicWrite;

    public JournalFileStore(IAppPaths paths) : this(paths, AtomicFile.WriteAllTextAsync)
    {
    }

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

        return Directory.EnumerateFiles(dir)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => string.Equals(Path.GetExtension(name), Extension, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

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
