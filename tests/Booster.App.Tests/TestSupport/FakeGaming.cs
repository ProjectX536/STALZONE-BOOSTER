using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Profiles;
using Booster.Core.Recovery;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Booster.App.Tests.TestSupport;

/// <summary>Игровая часть Core на фейках: журнал в памяти, процессы и время подставные, система не меняется.</summary>
internal sealed class FakeGaming : IAsyncDisposable
{
    public FakeGaming(FakeProfileRepository? profiles = null, FakeActionCatalog? catalog = null, IReadOnlyList<IActionRestorer>? restorers = null)
    {
        Profiles = profiles ?? new FakeProfileRepository(ProfileFactory.Profile(games: ["cs2"]));
        var policy = new ActionPolicy();
        var rollback = new RollbackManager(restorers ?? [], Journal, Time, NullLogger<RollbackManager>.Instance);
        var validator = new RecoveryValidator(restorers ?? [], Journal, Time, NullLogger<RecoveryValidator>.Instance);
        var manager = new GamingProfileManager(catalog ?? new FakeActionCatalog(), policy,
            new ActionExecutor(policy, Journal, Time, NullLogger<ActionExecutor>.Instance), rollback, validator,
            Journal, Processes, new FakeSystemLoad(), new FakeSensorProvider(Time),
            new SessionFrameCapture(new FakeFrameCaptureProvider(Time), Config, NullLogger<SessionFrameCapture>.Instance), Config, Time,
            NullLogger<GamingProfileManager>.Instance);
        Coordinator = new GameSessionCoordinator(manager, Processes, Profiles, Config, Time, NullLogger<GameSessionCoordinator>.Instance);
        Recovery = new StartupRecovery(Journal, rollback, validator, restorers ?? [], Profiles, Config, Time, NullLogger<StartupRecovery>.Instance);
    }

    public RecordingJournal Journal { get; } = new();

    public FakeTimeProvider Time { get; } = new(SessionFactory.Start);

    public FakeProcessProvider Processes { get; } = new();

    public FakeConfiguration Config { get; } = new();

    public FakeProfileRepository Profiles { get; }

    public GameSessionCoordinator Coordinator { get; }

    public StartupRecovery Recovery { get; }

    public ValueTask DisposeAsync() => Coordinator.DisposeAsync();
}
