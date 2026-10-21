namespace Booster.Core.Abstractions;

public interface IStateFileStore
{
    Task<string?> ReadAsync(string name, CancellationToken ct);

    Task WriteAsync(string name, string contents, CancellationToken ct);

    void Delete(string name);
}
