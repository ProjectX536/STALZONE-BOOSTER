using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Booster.Core.Abstractions;
using Booster.Core.Engine;

namespace Booster.Core.Recovery;

public sealed record ChangeEntry(
    string ActionId,
    string DisplayName,
    string Summary,
    bool IsReversible,
    bool RequiresReboot,
    ChangeStatus Status,
    ActionSnapshot Snapshot,
    JsonElement? Result,
    string? Detail,
    bool? Verified,
    DateTimeOffset UpdatedAt);

public sealed record SkippedAction(string ActionId, string DisplayName, string Reason);

public sealed record SessionRecord(
    int SchemaVersion,
    string SessionId,
    string ProfileId,
    string ProfileName,
    string GameName,
    int? GameProcessId,
    ExecutionTrigger Trigger,
    DateTimeOffset StartedAt,
    SessionState State,
    SessionOutcome? Outcome,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<ChangeEntry> Changes,
    IReadOnlyList<SkippedAction> Skipped,
    SessionKind Kind = SessionKind.Game)
{
    public const int CurrentSchemaVersion = 1;

    public bool IsFinished => Outcome is not null;
}

public static partial class SessionIds
{
    public static string New(DateTimeOffset now) =>
        now.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-"
        + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));

    public static bool IsValid(string? sessionId) => sessionId is not null && Pattern().IsMatch(sessionId);

    [GeneratedRegex("^[0-9]{8}-[0-9]{6}-[0-9a-f]{8}$")]
    private static partial Regex Pattern();
}
