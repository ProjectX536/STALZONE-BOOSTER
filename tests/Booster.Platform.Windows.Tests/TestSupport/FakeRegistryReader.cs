using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Tests.TestSupport;

/// <summary>Реестр в памяти: ключ → значения. Нет ключа — как в настоящем реестре, null и пустой список.</summary>
public sealed class FakeRegistryReader : IRegistryReader
{
    private readonly Dictionary<(RegistryRoot, string), Dictionary<string, object?>> _keys = [];
    private readonly HashSet<(RegistryRoot, string)> _denied = [];
    private readonly Dictionary<(RegistryRoot, string), DateTimeOffset> _writeTimes = [];

    /// <summary>Время записи ключа; без него ключ для <see cref="GetLastWriteTime"/> не существует.</summary>
    public FakeRegistryReader SetWriteTime(RegistryRoot root, string subKey, DateTimeOffset writtenAt)
    {
        _writeTimes[(root, subKey.ToUpperInvariant())] = writtenAt;
        return this;
    }

    public FakeRegistryReader Set(RegistryRoot root, string subKey, string valueName, object? value)
    {
        var key = (root, subKey.ToUpperInvariant());
        if (!_keys.TryGetValue(key, out var values))
            _keys[key] = values = new(StringComparer.OrdinalIgnoreCase);
        values[valueName] = value;
        return this;
    }

    /// <summary>Чтение ключа бросает <see cref="UnauthorizedAccessException"/>, как ключ с закрытым ACL.</summary>
    public FakeRegistryReader Deny(RegistryRoot root, string subKey)
    {
        _denied.Add((root, subKey.ToUpperInvariant()));
        return this;
    }

    public object? GetValue(RegistryRoot root, string subKey, string valueName) =>
        Find(root, subKey) is { } values ? values.GetValueOrDefault(valueName) : null;

    public IReadOnlyList<KeyValuePair<string, object?>> GetValues(RegistryRoot root, string subKey) =>
        Find(root, subKey) is { } values ? values.ToArray() : [];

    public DateTimeOffset? GetLastWriteTime(RegistryRoot root, string subKey)
    {
        _ = Find(root, subKey);
        return _writeTimes.TryGetValue((root, subKey.ToUpperInvariant()), out var writtenAt) ? writtenAt : null;
    }

    private Dictionary<string, object?>? Find(RegistryRoot root, string subKey)
    {
        var key = (root, subKey.ToUpperInvariant());
        if (_denied.Contains(key))
            throw new UnauthorizedAccessException($"Access to {root}\\{subKey} is denied");
        return _keys.GetValueOrDefault(key);
    }
}
