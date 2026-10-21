using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Profiles;

namespace Booster.Core.SystemHealth;

/// <summary>
/// Возврат настройки белого списка по снимку <see cref="RegistryTweakAction"/>: только ключи и имена значений из
/// <c>registryTweaks</c> каталога. Запись без имени значения разрешает любое непустое имя — путь к exe игры.
/// </summary>
public sealed class RegistryTweakRestorer(IUserRegistry registry, IBoosterConfiguration config) : UserRegistryRestorer(registry)
{
    public override string ActionId => ActionIds.RegistryTweak;

    protected override bool IsAllowed(string subKey, string valueName) =>
        config.Current.Catalog.RegistryTweaks.Any(t =>
            string.Equals(t.SubKey, subKey, StringComparison.OrdinalIgnoreCase)
            && (t.ValueName is null ? valueName.Length > 0 : string.Equals(t.ValueName, valueName, StringComparison.OrdinalIgnoreCase)));
}
