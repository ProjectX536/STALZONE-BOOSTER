using Booster.Core.Abstractions;
using Microsoft.Win32;

namespace Booster.Platform.Windows.Common;

/// <summary>
/// HKLM в 64-битном представлении, только ключи StartupApproved автозагрузки для всех пользователей. Чтение доступно без прав
/// администратора; запись без них отклоняет сама Windows (UnauthorizedAccessException), а действие до неё не доходит — его не
/// пропускает политика.
/// </summary>
public sealed class MachineRegistry : IMachineRegistry
{
    /// <summary>Точное совпадение без учёта регистра: вложенные ключи не разрешены.</summary>
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
