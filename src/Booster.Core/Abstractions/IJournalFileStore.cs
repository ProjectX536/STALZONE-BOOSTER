namespace Booster.Core.Abstractions;

public interface IJournalFileStore
{
    IReadOnlyList<string> List();

    Task<string> ReadAsync(string fileName, CancellationToken ct);

    Task WriteAsync(string fileName, string json, CancellationToken ct);

    void Delete(string fileName);
}
