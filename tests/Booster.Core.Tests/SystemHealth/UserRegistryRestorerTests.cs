using Booster.Core.Abstractions;
using Booster.Core.Recovery;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static Booster.Core.Tests.TestSupport.SessionFactory;

namespace Booster.Core.Tests.SystemHealth;

public class UserRegistryRestorerTests
{
    private const string Allowed = @"Software\Microsoft\GameBar";
    private const string Name = "AutoGameModeEnabled";

    private static readonly UserRegistryValue Zero = UserRegistryValue.FromDWord(0);
    private static readonly UserRegistryValue One = UserRegistryValue.FromDWord(1);

    private readonly FakeUserRegistry _registry = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class TestRestorer(IUserRegistry registry) : UserRegistryRestorer(registry)
    {
        public override string ActionId => "test";

        protected override bool IsAllowed(string subKey, string valueName) =>
            string.Equals(subKey, Allowed, StringComparison.OrdinalIgnoreCase);
    }

    private static ActionSnapshot Snapshot(UserRegistryValue? original, UserRegistryValue applied, string subKey = Allowed) =>
        UserRegistrySnapshot.Capture("test", subKey, Name, original, applied, Start);

    [Fact]
    public async Task Inspect_reports_original_applied_and_other()
    {
        var restorer = new TestRestorer(_registry);
        var snapshot = Snapshot(Zero, One);

        _registry[Allowed, Name] = Zero;
        Assert.Equal(ObservedState.Original, await restorer.InspectAsync(snapshot, Ct));
        _registry[Allowed, Name] = One;
        Assert.Equal(ObservedState.Applied, await restorer.InspectAsync(snapshot, Ct));
        _registry[Allowed, Name] = UserRegistryValue.FromDWord(2);
        Assert.Equal(ObservedState.Other, await restorer.InspectAsync(snapshot, Ct));
        _registry[Allowed, Name] = null;
        Assert.Equal(ObservedState.Other, await restorer.InspectAsync(snapshot, Ct));
        Assert.Equal(ObservedState.Original, await restorer.InspectAsync(Snapshot(null, One), Ct));
    }

    [Fact]
    public async Task Value_of_another_type_is_other_not_failure()
    {
        _registry.ReadFailure = new InvalidDataException("REG_QWORD");

        Assert.Equal(ObservedState.Other, await new TestRestorer(_registry).InspectAsync(Snapshot(Zero, One), Ct));
    }

    [Fact]
    public async Task Revert_of_absent_original_deletes_value()
    {
        _registry[Allowed, Name] = One;

        await new TestRestorer(_registry).RevertAsync(Snapshot(null, One), Ct);

        Assert.Null(_registry[Allowed, Name]);
        Assert.Equal(1, _registry.Deletes);
    }

    [Fact]
    public async Task Revert_writes_original_value()
    {
        _registry[Allowed, Name] = One;

        await new TestRestorer(_registry).RevertAsync(Snapshot(Zero, One), Ct);

        Assert.Equal(Zero, _registry[Allowed, Name]);
    }

    [Fact]
    public async Task Restorer_refuses_key_outside_allowed_list()
    {
        var restorer = new TestRestorer(_registry);
        var evil = Snapshot(null, One, subKey: @"Software\Evil");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => restorer.InspectAsync(evil, Ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => restorer.RevertAsync(evil, Ct));
        Assert.Equal((0, 0, 0), (_registry.Reads, _registry.Writes, _registry.Deletes));

        var mixed = evil with { OriginalValue = new UserRegistrySnapshot(Allowed, Name, null).ToJson() };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => restorer.RevertAsync(mixed, Ct));

        var time = new FakeTimeProvider(Start);
        var journal = new RecordingJournal();
        var rollback = new RollbackManager([restorer], journal, time, NullLogger<RollbackManager>.Instance);
        var change = new ChangeEntry("test", "Тест", "", IsReversible: true, RequiresReboot: false, ChangeStatus.Applied, evil, null,
            null, null, Start);
        var restored = await rollback.RestoreAsync(Session(changes: [change]), Ct);

        Assert.Equal(ChangeStatus.RestoreFailed, Assert.Single(restored.Changes).Status);
        Assert.Equal((0, 0, 0), (_registry.Reads, _registry.Writes, _registry.Deletes));
    }

    [Fact]
    public void Snapshot_round_trips_through_json()
    {
        var snapshot = new UserRegistrySnapshot(Allowed, Name, UserRegistryValue.FromBinary([3, 0, 0, 0, 0xAB]));

        var json = snapshot.ToJson();

        Assert.Equal(snapshot, UserRegistrySnapshot.Read(json));
        Assert.Equal("Binary", json.GetProperty("value").GetProperty("kind").GetString());
        Assert.Equal("03000000ab", json.GetProperty("value").GetProperty("data").GetString());
        Assert.Equal(new UserRegistrySnapshot(Allowed, Name, null), UserRegistrySnapshot.Read(new UserRegistrySnapshot(Allowed, Name, null).ToJson()));
    }

    [Fact]
    public void Value_helpers_use_documented_text_forms()
    {
        Assert.Equal(new UserRegistryValue(UserRegistryValueKind.DWord, "4294967295"), UserRegistryValue.FromDWord(uint.MaxValue));
        Assert.Equal(new UserRegistryValue(UserRegistryValueKind.String, "GpuPreference=2;"), UserRegistryValue.FromString("GpuPreference=2;"));
        Assert.Equal([3, 0, 0xFF], UserRegistryValue.FromBinary([3, 0, 0xFF]).ToBytes());
    }
}
