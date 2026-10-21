using Booster.Core.Recovery;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static Booster.Core.Tests.TestSupport.SessionFactory;

namespace Booster.Core.Tests.Recovery;

public class RecoveryValidatorTests
{
    private readonly FakeSystemState _state = new();
    private readonly RecordingJournal _journal = new();
    private readonly FakeTimeProvider _time = new(Start.AddHours(1));

    private RecoveryValidator Validator(params string[] ids) =>
        new(ids.Select(id => new FakeRestorer(_state, id)), _journal, _time, NullLogger<RecoveryValidator>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Restored_values_are_verified_without_changing_anything()
    {
        _state["a"] = "orig";
        _state["b"] = "orig";
        var session = Session(changes: [Change("a", status: ChangeStatus.Restored), Change("b", status: ChangeStatus.NeedsReboot)]);

        var result = await Validator("a", "b").VerifyAsync(session, Ct);

        Assert.All(result.Changes, c => Assert.True(c.Verified));
        Assert.Equal([ChangeStatus.Restored, ChangeStatus.NeedsReboot], result.Changes.Select(c => c.Status));
        Assert.Empty(_state.Calls);
        Assert.Same(result, Assert.Single(_journal.Saves));
    }

    [Fact]
    public async Task Value_changed_back_needs_manual_check()
    {
        _state["a"] = "boost";

        var result = await Validator("a").VerifyAsync(Session(changes: [Change("a", status: ChangeStatus.Restored)]), Ct);

        var entry = Assert.Single(result.Changes);
        Assert.False(entry.Verified);
        Assert.Equal(ChangeStatus.NeedsManualCheck, entry.Status);
        Assert.False(string.IsNullOrWhiteSpace(entry.Detail));
        Assert.Empty(_state.Calls);
    }

    [Fact]
    public async Task Gone_object_is_verified()
    {
        var result = await Validator("a").VerifyAsync(Session(changes: [Change("a", status: ChangeStatus.Restored)]), Ct);

        Assert.True(Assert.Single(result.Changes).Verified);
    }

    [Fact]
    public async Task Conflicts_and_irreversible_changes_are_not_verified()
    {
        _state["a"] = "user";
        _state["b"] = "boost";
        var session = Session(changes:
        [
            Change("a", status: ChangeStatus.RestoreConflict),
            Change("b", status: ChangeStatus.NeedsManualCheck, reversible: false),
        ]);

        var result = await Validator("a", "b").VerifyAsync(session, Ct);

        Assert.All(result.Changes, c => Assert.Null(c.Verified));
        Assert.Equal([ChangeStatus.RestoreConflict, ChangeStatus.NeedsManualCheck], result.Changes.Select(c => c.Status));
    }

    [Fact]
    public async Task Journal_save_failure_still_returns_verified_session()
    {
        _state["a"] = "orig";
        _journal.FailWith = _ => new IOException("disk full");

        var result = await Validator("a").VerifyAsync(Session(changes: [Change("a", status: ChangeStatus.Restored)]), Ct);

        Assert.True(Assert.Single(result.Changes).Verified);
    }

    [Fact]
    public async Task Missing_restorer_leaves_verification_unknown()
    {
        var result = await Validator().VerifyAsync(Session(changes: [Change("a", status: ChangeStatus.Restored)]), Ct);

        var entry = Assert.Single(result.Changes);
        Assert.Null(entry.Verified);
        Assert.Equal(ChangeStatus.Restored, entry.Status);
        Assert.False(string.IsNullOrWhiteSpace(entry.Detail));
    }
}
