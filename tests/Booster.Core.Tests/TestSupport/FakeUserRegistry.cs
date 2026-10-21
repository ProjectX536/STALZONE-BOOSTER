using Booster.Core.Abstractions;

namespace Booster.Core.Tests.TestSupport;

public sealed class FakeUserRegistry : IUserRegistry, IMachineRegistry
{
    private readonly Dictionary<string, UserRegistryValue> _values = new(StringComparer.OrdinalIgnoreCase);

    public int Reads { get; private set; }

    public int Writes { get; private set; }

    public int Deletes { get; private set; }

    public Exception? ReadFailure { get; set; }

    public Exception? WriteFailure { get; set; }

    public UserRegistryValue? this[string subKey, string valueName]
    {
        get => _values.GetValueOrDefault(Key(subKey, valueName));
        set
        {
            if (value is null)
                _values.Remove(Key(subKey, valueName));
            else
                _values[Key(subKey, valueName)] = value;
        }
    }

    public UserRegistryValue? Read(string subKey, string valueName)
    {
        Reads++;
        if (ReadFailure is { } failure)
            throw failure;
        return this[subKey, valueName];
    }

    public void Write(string subKey, string valueName, UserRegistryValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Writes++;
        if (WriteFailure is { } failure)
            throw failure;
        this[subKey, valueName] = value;
    }

    public void Delete(string subKey, string valueName)
    {
        Deletes++;
        if (WriteFailure is { } failure)
            throw failure;
        this[subKey, valueName] = null;
    }

    private static string Key(string subKey, string valueName) => subKey + "\0" + valueName;
}
