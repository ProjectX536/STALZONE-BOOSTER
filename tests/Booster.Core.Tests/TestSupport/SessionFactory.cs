using System.Text.Json;
using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Recovery;

namespace Booster.Core.Tests.TestSupport;

public static class SessionFactory
{
    public const string Id = "20261019-120000-0000000a";

    public static readonly DateTimeOffset Start = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    public static JsonElement Json(string value) => JsonSerializer.SerializeToElement(value);

    public static ChangeEntry Change(string actionId, string original = "orig", string applied = "boost",
        ChangeStatus status = ChangeStatus.Applied, bool reversible = true, bool requiresReboot = false) =>
        new(actionId, $"Действие {actionId}", $"{original} → {applied}", reversible, requiresReboot, status,
            new ActionSnapshot(1, actionId, Json(original), Json(applied), Start), null, null, null, Start);

    public static SessionRecord Session(string id = Id, IReadOnlyList<ChangeEntry>? changes = null,
        SessionState state = SessionState.Gaming, SessionOutcome? outcome = null, DateTimeOffset? startedAt = null,
        string profileId = "competitive") =>
        new(SessionRecord.CurrentSchemaVersion, id, profileId, "Competitive Gaming", "cs2", 4242, ExecutionTrigger.UserInitiated,
            startedAt ?? Start, state, outcome, outcome is null ? null : (startedAt ?? Start).AddHours(1), changes ?? [], []);

    public static SessionRecord Finished(string id, DateTimeOffset startedAt) =>
        Session(id, state: SessionState.Idle, outcome: SessionOutcome.Completed, startedAt: startedAt);
}
