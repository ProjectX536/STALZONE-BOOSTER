using Booster.Core.Abstractions;
using Booster.Core.Profiles;

namespace Booster.Core.SystemHealth;

/// <summary>
/// Возврат состояния элемента автозагрузки для всех пользователей по снимку <see cref="StartupItemAction"/>: только ключи
/// StartupApproved HKLM. Без прав администратора запись отклоняет сам реестр, и изменение остаётся невозвращённым.
/// </summary>
public sealed class MachineStartupItemRestorer(IMachineRegistry registry) : UserRegistryRestorer(registry)
{
    public override string ActionId => ActionIds.MachineStartupItem;

    // Безымянное значение «(По умолчанию)» элементом автозагрузки не бывает.
    protected override bool IsAllowed(string subKey, string valueName) =>
        valueName.Length > 0
        && (string.Equals(subKey, StartupApprovedKeys.Run, StringComparison.OrdinalIgnoreCase)
            || string.Equals(subKey, StartupApprovedKeys.Run32, StringComparison.OrdinalIgnoreCase)
            || string.Equals(subKey, StartupApprovedKeys.StartupFolder, StringComparison.OrdinalIgnoreCase));
}
