using Booster.Core.Abstractions;

namespace Booster.Core.Profiles;

/// <summary>Действия, которые Booster умеет выполнять, по id записи профиля.</summary>
public interface IActionCatalog
{
    /// <returns>null — id не реализован (registry-tweaks и другие recommend-only записи профиля).</returns>
    IReversibleAction? Create(string actionId, GamingProfile profile, GameProcess? game);
}
