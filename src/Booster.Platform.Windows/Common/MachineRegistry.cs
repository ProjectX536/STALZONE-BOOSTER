using Booster.Core.Abstractions;
using Microsoft.Win32;

namespace Booster.Platform.Windows.Common;

public sealed class MachineRegistry : IMachineRegistry
{
    public static IReadOnlyList<string> AllowedKeys { get; } =
    [
        StartupApprovedKeys.Run,
        StartupApprovedKeys.Run32,
        StartupApprovedKeys.StartupFolder,
    ];

    private readonly RegistryValueStore _store;

    public MachineRegistry()
        : this(() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
    {
    }

    internal MachineRegistry(Func<RegistryKey> openRoot) => _store = new RegistryValueStore("HKLM", AllowedKeys, openRoot);

    public UserRegistryValue? Read(string subKey, string valueName) => _store.Read(subKey, valueName);

    public void Write(string subKey, string valueName, UserRegistryValue value) => _store.Write(subKey, valueName, value);

    public void Delete(string subKey, string valueName) => _store.Delete(subKey, valueName);
}
