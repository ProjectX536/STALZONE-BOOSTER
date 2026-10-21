using System.Text.RegularExpressions;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Tests.TestSupport;

public sealed partial class FakeCimClient : ICimClient
{
    private readonly Dictionary<string, Func<CancellationToken, Task<IReadOnlyList<CimRow>>>> _classes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Namespace, string Wql)> _queries = [];

    public IReadOnlyList<(string Namespace, string Wql)> Queries
    {
        get { lock (_queries) return _queries.ToArray(); }
    }

    public static FakeCimClient FromFixture(string folder)
    {
        var fake = new FakeCimClient();
        foreach (var (className, rows) in WmiFixture.LoadFolder(folder))
            fake.With(className, rows);
        return fake;
    }

    public FakeCimClient With(string className, IReadOnlyList<CimRow> rows)
    {
        _classes[className] = _ => Task.FromResult(rows);
        return this;
    }

    public FakeCimClient Throw(string className, Exception exception)
    {
        _classes[className] = _ => Task.FromException<IReadOnlyList<CimRow>>(exception);
        return this;
    }

    public Task<IReadOnlyList<CimRow>> QueryAsync(string wmiNamespace, string wql, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_queries) _queries.Add((wmiNamespace, wql));

        var match = FromClause().Match(wql);
        if (!match.Success)
            throw new ArgumentException($"FakeCimClient: no FROM clause in '{wql}'", nameof(wql));
        var className = match.Groups[1].Value;
        if (!_classes.TryGetValue(className, out var answer))
            throw new InvalidOperationException($"FakeCimClient: nothing registered for {className}");
        return answer(ct);
    }

    [GeneratedRegex(@"\bFROM\s+(\w+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FromClause();
}
