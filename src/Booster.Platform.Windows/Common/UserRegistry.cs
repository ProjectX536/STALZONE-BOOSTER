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

    private readonly RegistryValueStore _store;

    public UserRegistry()
        : this(() => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
    {
    }

    internal UserRegistry(Func<RegistryKey> openRoot) => _store = new RegistryValueStore("HKCU", AllowedKeys, openRoot);

    public UserRegistryValue? Read(string subKey, string valueName) => _store.Read(subKey, valueName);

    public void Write(string subKey, string valueName, UserRegistryValue value) => _store.Write(subKey, valueName, value);

    public void Delete(string subKey, string valueName) => _store.Delete(subKey, valueName);
}
