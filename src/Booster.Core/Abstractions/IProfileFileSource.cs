namespace Booster.Core.Abstractions;

public interface IProfileFileSource
{
    IReadOnlyList<string> ListBuiltIn();

    string ReadBuiltIn(string fileName);

    string? ReadUser(string fileName);

    Task WriteUserAsync(string fileName, string json, CancellationToken ct);
}
