using System.Globalization;
using Booster.Core.Abstractions;
using Microsoft.Win32;

namespace Booster.Platform.Windows.Common;

/// <summary>
/// HKCU в 64-битном представлении, только ключи из <see cref="AllowedKeys"/> — защита в глубину поверх проверок действий:
/// ключ вне списка отклоняется до открытия реестра. Права администратора не нужны.
/// </summary>
public sealed class UserRegistry : IUserRegistry
{
    /// <summary>Точное совпадение без учёта регистра: вложенные ключи не разрешены.</summary>
    public static IReadOnlyList<string> AllowedKeys { get; } =
    [
        StartupApprovedKeys.Run,
        StartupApprovedKeys.StartupFolder,
        @"Software\Microsoft\GameBar",
        @"Software\Microsoft\DirectX\UserGpuPreferences",
    ];

    private readonly Func<RegistryKey> _openRoot;

    public UserRegistry()
        : this(() => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
    {
    }

    internal UserRegistry(Func<RegistryKey> openRoot) => _openRoot = openRoot;

    public UserRegistryValue? Read(string subKey, string valueName)
    {
        EnsureAllowed(subKey, valueName);
        using var root = _openRoot();
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

        using var root = _openRoot();
        using var key = root.CreateSubKey(subKey, writable: true);
        key.SetValue(valueName, data, kind);
    }

    public void Delete(string subKey, string valueName)
    {
        EnsureAllowed(subKey, valueName);
        using var root = _openRoot();
        using var key = root.OpenSubKey(subKey, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    private static void EnsureAllowed(string subKey, string valueName)
    {
        ArgumentNullException.ThrowIfNull(subKey);
        ArgumentNullException.ThrowIfNull(valueName);
        if (!AllowedKeys.Contains(subKey, StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Registry key HKCU\\{subKey} is outside the list Booster may change");
    }
}
