using Booster.Core.Abstractions;

namespace Booster.Core.Tests.TestSupport;

public sealed class InMemoryJournalFileStore : IJournalFileStore
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Unreadable { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Deleted { get; } = [];

    public int Writes { get; private set; }

    public TaskCompletionSource? WriteGate { get; set; }

    public IReadOnlyDictionary<string, string> Files
    {
        get { lock (_files) return new Dictionary<string, string>(_files, StringComparer.OrdinalIgnoreCase); }
    }

    public void Put(string fileName, string json)
    {
        lock (_files) _files[fileName] = json;
    }

    public IReadOnlyList<string> List()
    {
        lock (_files) return _files.Keys.Order(StringComparer.Ordinal).ToArray();
    }

    public Task<string> ReadAsync(string fileName, CancellationToken ct)
    {
        if (Unreadable.Contains(fileName))
            throw new IOException($"{fileName} is locked");
        lock (_files) return Task.FromResult(_files[fileName]);
    }

    public async Task WriteAsync(string fileName, string json, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (WriteGate is { } gate)
            await gate.Task.WaitAsync(ct).ConfigureAwait(false);
        lock (_files)
        {
            _files[fileName] = json;
            Writes++;
        }
    }

    public void Delete(string fileName)
    {
        lock (_files)
        {
            _files.Remove(fileName);
            Deleted.Add(fileName);
        }
    }
}
