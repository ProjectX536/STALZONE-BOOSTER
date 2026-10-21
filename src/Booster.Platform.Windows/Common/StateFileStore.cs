using Booster.Core.Abstractions;

namespace Booster.Platform.Windows.Common;

/// <summary>Файлы состояния в <see cref="IAppPaths.State"/>; запись через <see cref="AtomicFile"/>.</summary>
public sealed class StateFileStore(IAppPaths paths) : IStateFileStore
{
    /// <summary>Чтение не мешает замене файла записью Booster: доступ на запись и удаление разделяется.</summary>
    public async Task<string?> ReadAsync(string name, CancellationToken ct)
    {
        var path = PathOf(name);
        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        await using (stream.ConfigureAwait(false))
        {
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }
    }

    public Task WriteAsync(string name, string contents, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contents);
        return AtomicFile.WriteAllTextAsync(PathOf(name), contents, ct);
    }

    public void Delete(string name) => File.Delete(PathOf(name));

    private string PathOf(string name) => Path.Combine(paths.State, FileNames.CheckBare(name));
}
