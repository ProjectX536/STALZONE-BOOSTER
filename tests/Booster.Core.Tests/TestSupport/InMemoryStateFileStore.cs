using Booster.Core.Abstractions;

namespace Booster.Core.Tests.TestSupport;

/// <summary>Каталог state в памяти: имя файла → содержимое.</summary>
public sealed class InMemoryStateFileStore : IStateFileStore
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Deleted { get; } = [];

    public IReadOnlyDictionary<string, string> Files
    {
        get { lock (_files) return new Dictionary<string, string>(_files, StringComparer.OrdinalIgnoreCase); }
    }

    public void Put(string name, string contents)
    {
        lock (_files) _files[name] = contents;
    }

    public bool Contains(string name)
    {
        lock (_files) return _files.ContainsKey(name);
    }

    public Task<string?> ReadAsync(string name, CancellationToken ct)
    {
        lock (_files) return Task.FromResult(_files.TryGetValue(name, out var contents) ? contents : null);
    }

    public Task WriteAsync(string name, string contents, CancellationToken ct)
    {
        Put(name, contents);
        return Task.CompletedTask;
    }

    public void Delete(string name)
    {
        lock (_files)
        {
            _files.Remove(name);
            Deleted.Add(name);
        }
    }
}
