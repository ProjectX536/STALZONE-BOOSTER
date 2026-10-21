using Booster.Core.Abstractions;
using Booster.Core.Profiles.Actions;

namespace Booster.Core.Profiles;

public sealed class ActionCatalog(IPowerPlanProvider power, IProcessPriorityController priorities, IProcessProvider processes,
    IAppCloser closer, TimeProvider time) : IActionCatalog
{
    public IReversibleAction? Create(string actionId, GamingProfile profile, GameProcess? game)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        ArgumentNullException.ThrowIfNull(profile);

        var entry = profile.FindAction(actionId);
        return actionId switch
        {
            ActionIds.PowerPlan => new PowerPlanAction(power, TargetSchemes(entry), time),
            ActionIds.GamePriority => GamePriority(entry) is { } target ? new GamePriorityAction(priorities, game, target, time) : null,
            ActionIds.BackgroundApps => new BackgroundAppsAction(processes, closer, profile, game, time),
            _ => null,
        };
    }

    private static Guid[] TargetSchemes(ProfileActionEntry? entry) =>
        (entry?.TargetSchemes ?? []).Select(s => Guid.TryParse(s, out var id) ? id : (Guid?)null).OfType<Guid>().ToArray();

    private static ProcessPriority? GamePriority(ProfileActionEntry? entry) => entry?.Value switch
    {
        null or nameof(ProcessPriority.AboveNormal) => ProcessPriority.AboveNormal,
        nameof(ProcessPriority.High) => ProcessPriority.High,
        _ => null,
    };
}
