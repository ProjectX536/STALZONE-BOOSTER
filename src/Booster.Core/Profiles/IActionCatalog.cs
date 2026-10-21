using Booster.Core.Abstractions;

namespace Booster.Core.Profiles;

public interface IActionCatalog
{
    IReversibleAction? Create(string actionId, GamingProfile profile, GameProcess? game);
}
