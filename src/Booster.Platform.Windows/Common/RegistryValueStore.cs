using System.Globalization;
using Booster.Core.Abstractions;
using Microsoft.Win32;

namespace Booster.Platform.Windows.Common;

/// <summary>
/// Чтение и запись значений одного раздела реестра, только ключи из списка — защита в глубину поверх проверок действий: ключ вне
/// списка отклоняется до открытия реестра. Точное совпадение без учёта регистра: вложенные ключи не разрешены.
/// </summary>
internal sealed class RegistryValueStore(string hiveName, IReadOnlyList<string> allowedKeys, Func<RegistryKey> openRoot) : IRegistryValues
{
    public UserRegistryValue? Read(string subKey, string valueName)
    {
        EnsureAllowed(subKey, valueName);
        using var root = openRoot();
        using var key = root.OpenSubKey(subKey, writable: false);
        if (key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not { } value)
            return null;

        // REG_EXPAND_SZ и прочие типы Booster не пишет: прочитать их как строку значило бы при откате сменить тип значения.
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
