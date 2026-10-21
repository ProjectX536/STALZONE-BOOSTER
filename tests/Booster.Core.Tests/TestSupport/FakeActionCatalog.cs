using Booster.Core.Abstractions;
using Booster.Core.Profiles;

namespace Booster.Core.Tests.TestSupport;

public sealed class FakeActionCatalog : IActionCatalog
{
    private readonly Dictionary<string, IReversibleAction> _actions = new(StringComparer.Ordinal);

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
