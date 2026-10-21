using Booster.Core.Abstractions;

namespace Booster.Core.Tests.TestSupport;

public sealed class InMemoryProfileFileSource : IProfileFileSource
{
    private readonly Dictionary<string, string> _builtIn = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _user = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> UnreadableUser { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> UserReads { get; } = [];

    public static InMemoryProfileFileSource WithShippedCompetitive() =>
        new InMemoryProfileFileSource().PutBuiltIn("Competitive.json", ProfileFactory.ShippedCompetitiveJson());

    public InMemoryProfileFileSource PutBuiltIn(string fileName, string json)
    {
        _builtIn[fileName] = json;
        return this;
    }

    public InMemoryProfileFileSource PutUser(string fileName, string json)
    {
        _user[fileName] = json;
        return this;
    }

    public string? User(string fileName) => _user.GetValueOrDefault(fileName);

    public IReadOnlyList<string> ListBuiltIn() => _builtIn.Keys.Order(StringComparer.Ordinal).ToArray();

    public string ReadBuiltIn(string fileName) => _builtIn[fileName];

    public string? ReadUser(string fileName)
    {
        UserReads.Add(fileName);
        if (UnreadableUser.Contains(fileName))
            throw new IOException($"{fileName} is locked");
        return _user.GetValueOrDefault(fileName);
    }

    public Task WriteUserAsync(string fileName, string json, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _user[fileName] = json;
        return Task.CompletedTask;
    }
}
