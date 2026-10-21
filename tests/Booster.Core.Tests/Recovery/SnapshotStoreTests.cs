using System.Text.Json;
using Booster.Core.Engine;
using Booster.Core.Recovery;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.Recovery;

public class SnapshotStoreTests
{
    private static SessionRecord TwoChanges() =>
        SessionFactory.Session(changes:
        [
            SessionFactory.Change("power-plan", "balanced", "high") with
            {
                Result = JsonSerializer.SerializeToElement(new { closed = new[] { "chrome" } }),
                Detail = "Закрыто: chrome",
                Verified = true,
            },
            SessionFactory.Change("game-priority", "Normal", "AboveNormal", ChangeStatus.Pending),
        ]) with
        {
            Skipped = [new SkippedAction("user-selected-background-apps", "Фоновые приложения", "нужно подтверждение")],
        };

    [Fact]
    public void Session_survives_a_round_trip()
    {
        var session = TwoChanges();

        var read = SnapshotStore.Deserialize($"{session.SessionId}.json", SnapshotStore.Serialize(session));

        Assert.Null(read.Problem);
        var copy = Assert.IsType<SessionRecord>(read.Session);
        Assert.Equal(session.SessionId, copy.SessionId);
        Assert.Equal(session.StartedAt, copy.StartedAt);
        Assert.Equal(session.Trigger, copy.Trigger);
        Assert.Equal(session.GameProcessId, copy.GameProcessId);
        Assert.Null(copy.Outcome);
        Assert.Equal(2, copy.Changes.Count);
        var power = copy.Changes[0];
        Assert.Equal("power-plan", power.ActionId);
        Assert.Equal(ChangeStatus.Applied, power.Status);
        Assert.Equal("balanced", power.Snapshot.OriginalValue.GetString());
        Assert.Equal("high", power.Snapshot.AppliedValue?.GetString());
        Assert.Equal("chrome", power.Result?.GetProperty("closed")[0].GetString());
        Assert.Equal("Закрыто: chrome", power.Detail);
        Assert.True(power.Verified);
        Assert.Null(copy.Changes[1].Result);
        Assert.Null(copy.Changes[1].Verified);
        Assert.Equal("нужно подтверждение", Assert.Single(copy.Skipped).Reason);
    }

    [Fact]
    public void Enums_are_written_as_names()
    {
        var json = SnapshotStore.Serialize(TwoChanges());

        Assert.Contains("\"status\": \"Applied\"", json);
        Assert.Contains("\"state\": \"Gaming\"", json);
        Assert.Contains("\"trigger\": \"UserInitiated\"", json);
        Assert.Contains("\"schemaVersion\": 1", json);
    }

    [Fact]
    public void Newer_schema_is_reported_and_not_read()
    {
        var json = SnapshotStore.Serialize(TwoChanges()).Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 2,");

        var read = SnapshotStore.Deserialize($"{SessionFactory.Id}.json", json);

        Assert.Null(read.Session);
        Assert.Equal(JournalProblemKind.NewerSchemaVersion, read.Problem?.Kind);
        Assert.Equal($"{SessionFactory.Id}.json", read.Problem?.FileName);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"schemaVersion\": 1}")]
    [InlineData("{\"sessionId\": \"20261019-120000-0000000a\"}")]
    public void Broken_file_is_reported_without_throwing(string json)
    {
        var read = SnapshotStore.Deserialize($"{SessionFactory.Id}.json", json);

        Assert.Null(read.Session);
        Assert.Equal(JournalProblemKind.InvalidJson, read.Problem?.Kind);
    }

    [Fact]
    public void Missing_required_field_is_invalid()
    {
        using var doc = JsonDocument.Parse(SnapshotStore.Serialize(TwoChanges()));
        var withoutChanges = doc.RootElement.EnumerateObject().Where(p => p.Name != "changes")
            .ToDictionary(p => p.Name, p => p.Value);

        var read = SnapshotStore.Deserialize($"{SessionFactory.Id}.json", JsonSerializer.Serialize(withoutChanges));

        Assert.Equal(JournalProblemKind.InvalidJson, read.Problem?.Kind);
    }

    [Fact]
    public void File_name_must_match_session_id()
    {
        var read = SnapshotStore.Deserialize("20261019-130000-0000000b.json", SnapshotStore.Serialize(TwoChanges()));

        Assert.Null(read.Session);
        Assert.Equal(JournalProblemKind.InvalidJson, read.Problem?.Kind);
    }

    [Fact]
    public void Session_id_is_utc_time_and_random_suffix()
    {
        var id = SessionIds.New(new DateTimeOffset(2026, 10, 19, 15, 30, 12, TimeSpan.FromHours(3)));

        Assert.StartsWith("20261019-123012-", id);
        Assert.True(SessionIds.IsValid(id));
        Assert.NotEqual(id, SessionIds.New(new DateTimeOffset(2026, 10, 19, 15, 30, 12, TimeSpan.FromHours(3))));
    }

    [Theory]
    [InlineData(@"..\x")]
    [InlineData("20261019-123012-ABCDEF12")]
    [InlineData("20261019-123012-abcdef1")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_session_ids_are_rejected(string? id)
    {
        Assert.False(SessionIds.IsValid(id));
    }

    [Fact]
    public void Finished_is_defined_by_outcome()
    {
        Assert.False(SessionFactory.Session().IsFinished);
        Assert.True(SessionFactory.Session(outcome: SessionOutcome.Completed).IsFinished);
        Assert.Equal(ExecutionTrigger.UserInitiated, SessionFactory.Session().Trigger);
    }
}
