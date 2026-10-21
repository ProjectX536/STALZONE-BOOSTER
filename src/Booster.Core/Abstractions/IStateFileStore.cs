namespace Booster.Core.Abstractions;

/// <summary>Небольшие файлы состояния в %LOCALAPPDATA%\Booster\state; запись атомарна. Только «голые» имена файлов, без путей.</summary>
public interface IStateFileStore
{
    /// <returns>null — файла нет.</returns>
    Task<string?> ReadAsync(string name, CancellationToken ct);

    Task WriteAsync(string name, string contents, CancellationToken ct);

    /// <summary>Файла нет — ничего не делает.</summary>
    void Delete(string name);
}
