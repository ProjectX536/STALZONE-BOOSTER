using Booster.Core.Abstractions;

namespace Booster.Core.Profiles;

public sealed record GameMatch(GamingProfile Profile, string GameName, IReadOnlyList<int> ProcessIds);

public static class ProfileMatcher
{
    public static GameMatch? Match(IReadOnlyList<ProcessInfo> running, IReadOnlyList<GamingProfile> candidates) =>
        MatchAll(running, candidates).FirstOrDefault();

    public static IReadOnlyList<GameMatch> MatchAll(IReadOnlyList<ProcessInfo> running, IReadOnlyList<GamingProfile> candidates)
    {
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(candidates);

        var byName = running
            .Select(p => (Name: ProcessNames.Normalize(p.Name), Process: p))
            .Where(p => p.Name is not null)
            .ToLookup(p => p.Name!, p => p.Process, StringComparer.OrdinalIgnoreCase);

        var matches = new List<GameMatch>();
        foreach (var profile in candidates)
        {
            foreach (var game in profile.Games)
            {
                if (ProcessNames.Normalize(game) is not { } name)
                    continue;
                var processes = byName[name].OrderBy(p => p.ProcessId).ToArray();
                if (processes.Length > 0)
                    matches.Add(new GameMatch(profile, processes[0].Name, processes.Select(p => p.ProcessId).ToArray()));
            }
        }

        return matches;
    }
}
