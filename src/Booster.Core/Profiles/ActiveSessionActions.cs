using Booster.Core.Abstractions;
using Booster.Core.Recovery;

namespace Booster.Core.Profiles;

public sealed class ActiveSessionActions(GameSessionCoordinator coordinator) : IActiveSessionActions
{
    public IReadOnlyList<string> Current() => AppliedIn(coordinator.Status.Progress?.Session);

    internal static IReadOnlyList<string> AppliedIn(SessionRecord? session) =>
        session?.Changes.Where(c => c.Status == ChangeStatus.Applied).Select(c => c.ActionId).Distinct(StringComparer.Ordinal).ToArray() ?? [];
}
