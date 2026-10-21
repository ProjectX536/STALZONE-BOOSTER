using Booster.Core.Abstractions;
using Microsoft.Win32;

namespace Booster.Platform.Windows.Common;

public sealed class UserRegistry : IUserRegistry
{
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
