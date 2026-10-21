using Booster.Core.Abstractions;
using Booster.Core.Profiles;

namespace Booster.Core.Tests.TestSupport;

/// <summary>Каталог из заранее созданных действий; id без действия — null, как у нереализованных записей профиля.</summary>
public sealed class FakeActionCatalog : IActionCatalog
{
    private readonly Dictionary<string, IReversibleAction> _actions = new(StringComparer.Ordinal);

    /// <summary>Игра, переданная в последний <see cref="Create"/>.</summary>
    public GameProcess? LastGame { get; private set; }

    public FakeActionCatalog Add(IReversibleAction action)
    {
        lock (_actions) _actions[action.Id] = action;
        return this;
    }

    public IReversibleAction? Create(string actionId, GamingProfile profile, GameProcess? game)
    {
        lock (_actions)
        {
            LastGame = game;
            return _actions.GetValueOrDefault(actionId);
        }
    }
}
