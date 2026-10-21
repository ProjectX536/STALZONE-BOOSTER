using Booster.App.Resources;
using Booster.App.Services;
using Booster.App.Tests.TestSupport;
using Booster.App.ViewModels;
using Booster.Core.Abstractions;
using Booster.Core.Profiles;
using Booster.Core.Recovery;
using Booster.Core.Reports;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booster.App.Tests.ViewModels;

public sealed class JournalViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Recovery_failure_is_listed_as_a_problem()
    {
        await using var gaming = new FakeGaming();
        gaming.Journal.LoadFailure = new IOException("journal is locked");
        var lifecycle = new GamingLifecycle(gaming.Recovery, gaming.Coordinator, NullLogger<GamingLifecycle>.Instance);
        await lifecycle.StartAsync();
        var maintenance = TestMaintenance.Create(gaming.Journal, new FakeUserRegistry(), gaming.Config, gaming.Time);
        var journal = new JournalViewModel(gaming.Journal, gaming.Recovery, lifecycle, gaming.Coordinator, maintenance,
            NullLogger<JournalViewModel>.Instance);

        journal.Activate();

        Assert.True(journal.HasRecoveryReport);
        Assert.Equal(["Восстановление при запуске не выполнено: journal is locked"], journal.Problems);
    }

    [Fact]
    public async Task Revert_maintenance_session_restores_value()
    {
        await using var gaming = new FakeGaming();
        var registry = new FakeUserRegistry();
        var changes = TestMaintenance.Journal();
        var maintenance = TestMaintenance.Create(changes, registry, gaming.Config, gaming.Time);
        var entry = new StartupEntry("Discord", StartupLocation.UserRun, "Discord.exe", @"C:\Discord\Discord.exe", StartupState.Enabled);
        var applied = await maintenance.ApplyAsync(new StartupItemAction(registry, new FakeUserRegistry(), entry, gaming.Time), userConfirmed: true,
            gamingSessionActive: false, Ct);
        Assert.Equal(MaintenanceStatus.Applied, applied.Status);
        Assert.NotNull(registry[StartupApprovedKeys.Run, "Discord"]);

        var lifecycle = new GamingLifecycle(gaming.Recovery, gaming.Coordinator, NullLogger<GamingLifecycle>.Instance);
        var journal = new JournalViewModel(changes, gaming.Recovery, lifecycle, gaming.Coordinator, maintenance,
            NullLogger<JournalViewModel>.Instance);
        await journal.LoadCommand.ExecuteAsync(null);
        var row = Assert.Single(journal.Sessions);
        Assert.Equal("Обслуживание", row.KindText);
        Assert.Same(row, journal.SelectedSession);
        Assert.True(journal.RevertMaintenanceCommand.CanExecute(null));

        await journal.RevertMaintenanceCommand.ExecuteAsync(null);

        Assert.Null(registry[StartupApprovedKeys.Run, "Discord"]);
        Assert.Equal(Strings.MaintenanceReverted, journal.StatusMessage);
        Assert.Equal(EnumText.Format(ChangeStatus.Restored), Assert.Single(journal.SelectedChanges).StatusText);
        Assert.False(journal.RevertMaintenanceCommand.CanExecute(null));
    }

    [Fact]
    public async Task Revert_is_not_offered_for_game_sessions()
    {
        await using var gaming = new FakeGaming();
        var changes = TestMaintenance.Journal();
        await changes.SaveAsync(SessionFactory.Session(changes: [SessionFactory.Change("a")], outcome: SessionOutcome.Completed), Ct);
        var lifecycle = new GamingLifecycle(gaming.Recovery, gaming.Coordinator, NullLogger<GamingLifecycle>.Instance);
        var journal = new JournalViewModel(changes, gaming.Recovery, lifecycle, gaming.Coordinator,
            TestMaintenance.Create(changes, new FakeUserRegistry(), gaming.Config, gaming.Time), NullLogger<JournalViewModel>.Instance);

        await journal.LoadCommand.ExecuteAsync(null);

        Assert.NotNull(journal.SelectedSession);
        Assert.False(journal.RevertMaintenanceCommand.CanExecute(null));
    }
}
