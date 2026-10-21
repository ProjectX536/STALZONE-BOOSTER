namespace Booster.Core.Tests.TestSupport;

/// <summary>
/// «Система» для фейковых действий: id действия → текущее значение. Нет ключа — объекта нет.
/// <see cref="Calls"/> — общий журнал изменяющих вызовов всех фейков (<c>apply:a</c>, <c>revert:b</c>).
/// </summary>
public sealed class FakeSystemState
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly List<string> _calls = [];

    public IReadOnlyList<string> Calls
    {
        get { lock (_calls) return _calls.ToArray(); }
    }

    public string? this[string id]
    {
        get { lock (_values) return _values.GetValueOrDefault(id); }
        set
        {
            lock (_values)
            {
                if (value is null)
                    _values.Remove(id);
                else
                    _values[id] = value;
            }
        }
    }

    public void Record(string call)
    {
        lock (_calls) _calls.Add(call);
    }

    public void ClearCalls()
    {
        lock (_calls) _calls.Clear();
    }
}
