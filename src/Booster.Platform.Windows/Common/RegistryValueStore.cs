using System.Globalization;
using Booster.Core.Abstractions;
using Microsoft.Win32;

namespace Booster.Platform.Windows.Common;

internal sealed class RegistryValueStore(string hiveName, IReadOnlyList<string> allowedKeys, Func<RegistryKey> openRoot) : IRegistryValues
{
    public UserRegistryValue? Read(string subKey, string valueName)
    {
        EnsureAllowed(subKey, valueName);
        using var root = openRoot();
        using var key = root.OpenSubKey(subKey, writable: false);
        if (key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not { } value)
            return null;

        // Другие типы не пишем: при откате тип значения поменялся бы.
        return key.GetValueKind(valueName) switch
        {
            RegistryValueKind.DWord => UserRegistryValue.FromDWord(unchecked((uint)(int)value)),
            RegistryValueKind.String => UserRegistryValue.FromString((string)value),
            RegistryValueKind.Binary => UserRegistryValue.FromBinary((byte[])value),
            var kind => throw new InvalidDataException($"Registry value {valueName} is {kind}"),
        };
    }

    public void Write(string subKey, string valueName, UserRegistryValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        EnsureAllowed(subKey, valueName);
        var (data, kind) = value.Kind switch
        {
            UserRegistryValueKind.DWord => ((object)unchecked((int)uint.Parse(value.Data, NumberStyles.None, CultureInfo.InvariantCulture)),
                RegistryValueKind.DWord),
            UserRegistryValueKind.String => (value.Data, RegistryValueKind.String),
            UserRegistryValueKind.Binary => (value.ToBytes(), RegistryValueKind.Binary),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value.Kind, null),
        };

        using var root = openRoot();
        using var key = root.CreateSubKey(subKey, writable: true);
        key.SetValue(valueName, data, kind);
    }

    public void Delete(string subKey, string valueName)
    {
        EnsureAllowed(subKey, valueName);
        using var root = openRoot();
        using var key = root.OpenSubKey(subKey, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    private void EnsureAllowed(string subKey, string valueName)
    {
        ArgumentNullException.ThrowIfNull(subKey);
        ArgumentNullException.ThrowIfNull(valueName);
        if (!allowedKeys.Contains(subKey, StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Registry key {hiveName}\\{subKey} is outside the list Booster may change");
    }
}
