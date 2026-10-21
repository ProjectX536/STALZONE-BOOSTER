using Booster.Core.Configuration;

namespace Booster.Core.Profiles;

public static class ProfileNormalizer
{
    private const int MinimumSampleIntervalMs = 1000;

    private const int MinimumCloseTimeoutMs = 1000;

    private static readonly string[] SupportedPriorities = ["AboveNormal", "High"];

    private const string DefaultPriority = "AboveNormal";

    public static ConfigLoadResult<GamingProfile> Normalize(GamingProfile profile, string fileName)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var warnings = new List<ConfigWarning>();
        var changed = false;

        void Clamped(string detail)
        {
            warnings.Add(new(ConfigWarningKind.ValueClamped, fileName, detail));
            changed = true;
        }

        void Removed(string detail)
        {
            warnings.Add(new(ConfigWarningKind.InvalidValue, fileName, detail));
            changed = true;
        }

        int AtLeast(int value, int minimum, string key)
        {
            if (value >= minimum)
                return value;
            Clamped($"{key}: {value} -> {minimum}");
            return minimum;
        }

        bool Required(bool value, string key)
        {
            if (!value)
                Clamped($"{key}: false -> true (обязательно по модели безопасности)");
            return true;
        }

        IReadOnlyList<string> Names(IReadOnlyList<string> names, string key, Func<string, string?>? rejection = null)
        {
            var result = new List<string>(names.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in names)
            {
                var name = ProcessNames.Normalize(raw);
                if (name is null)
                    Removed($"{key}: '{raw}' is not a file name");
                else if (!seen.Add(name))
                    Removed($"{key}: '{raw}' is a duplicate");
                else if (rejection?.Invoke(name) is { } reason)
                    Removed($"{key}: '{name}' {reason}");
                else
                {
                    changed |= !string.Equals(name, raw, StringComparison.Ordinal);
                    result.Add(name);
                }
            }

            return result;
        }

        ProfileActionEntry? Priority(ProfileActionEntry entry)
        {
            if (entry.Value is null)
            {
                changed = true;
                return entry with { Value = DefaultPriority };
            }

            var match = SupportedPriorities.FirstOrDefault(p => p.Equals(entry.Value.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                Removed($"actions.{entry.Id}.value: '{entry.Value}' is not AboveNormal or High; action removed");
                return null;
            }

            if (string.Equals(match, entry.Value, StringComparison.Ordinal))
                return entry;
            changed = true;
            return entry with { Value = match };
        }

        ProfileActionEntry? PowerPlan(ProfileActionEntry entry)
        {
            var schemes = new List<string>();
            foreach (var scheme in entry.TargetSchemes ?? [])
            {
                if (Guid.TryParse(scheme, out var guid))
                    schemes.Add(guid.ToString("D"));
                else
                    Removed($"actions.{entry.Id}.targetSchemes: '{scheme}' is not a GUID");
            }

            if (schemes.Count == 0)
            {
                Removed($"actions.{entry.Id}: no valid targetSchemes; action removed");
                return null;
            }

            if (entry.TargetSchemes is { } original && schemes.SequenceEqual(original, StringComparer.Ordinal))
                return entry;
            changed = true;
            return entry with { TargetSchemes = schemes };
        }

        var games = Names(profile.Games, "games", name => ProtectedProcesses.IsProtected(name) ? "is protected" : null);
        var gameSet = new HashSet<string>(games, StringComparer.OrdinalIgnoreCase);
        var userSelected = Names(profile.BackgroundApps.UserSelected, "backgroundApps.userSelected",
            name => ProtectedProcesses.IsProtected(name) ? "is protected"
                : gameSet.Contains(name) ? "is a game of this profile"
                : null);
        var enabledOptional = Names(profile.EnabledOptionalActions, "enabledOptionalActions");

        var actions = new List<ProfileActionEntry>(profile.Actions.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in profile.Actions)
        {
            if (entry is null)
            {
                Removed("actions: empty entry");
                continue;
            }

            if (!ids.Add(entry.Id))
            {
                Removed($"actions: duplicate '{entry.Id}'; first entry kept");
                continue;
            }

            var normalized = entry.Id switch
            {
                ActionIds.GamePriority => Priority(entry),
                ActionIds.PowerPlan => PowerPlan(entry),
                _ => entry,
            };
            if (normalized is not null)
                actions.Add(normalized);
        }

        var monitoring = profile.Monitoring with
        {
            SampleIntervalMs = AtLeast(profile.Monitoring.SampleIntervalMs, MinimumSampleIntervalMs, "monitoring.sampleIntervalMs"),
        };
        var backgroundApps = new BackgroundAppsSettings(
            AtLeast(profile.BackgroundApps.CloseTimeoutMs, MinimumCloseTimeoutMs, "backgroundApps.closeTimeoutMs"), userSelected);
        var recovery = profile.Recovery with
        {
            SnapshotBeforeChanges = Required(profile.Recovery.SnapshotBeforeChanges, "recovery.snapshotBeforeChanges"),
            VerifyAfterRestore = Required(profile.Recovery.VerifyAfterRestore, "recovery.verifyAfterRestore"),
        };

        if (!changed)
            return new(profile, []);
        return new(profile with
        {
            Games = games,
            EnabledOptionalActions = enabledOptional,
            Monitoring = monitoring,
            Actions = actions,
            BackgroundApps = backgroundApps,
            Recovery = recovery,
        }, warnings);
    }
}
