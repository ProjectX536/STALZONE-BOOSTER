namespace Booster.Core.Abstractions;

/// <summary>Файлы каталога журнала сессий. Только «голые» имена файлов, без путей.</summary>
public interface IJournalFileStore
{
    /// <summary>Имена <c>*.json</c> по возрастанию. Оставшиеся от прерванной записи <c>*.json.tmp</c> удаляются.</summary>
    IReadOnlyList<string> List();

    Task<string> ReadAsync(string fileName, CancellationToken ct);

    /// <summary>Атомарно: временный файл, сброс на диск, замена.</summary>
    Task WriteAsync(string fileName, string json, CancellationToken ct);

    void Delete(string fileName);
}
