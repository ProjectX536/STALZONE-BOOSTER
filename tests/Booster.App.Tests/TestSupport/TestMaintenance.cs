using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Engine;
using Booster.Core.Profiles;
using Booster.Core.Recovery;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booster.App.Tests.TestSupport;

/// <summary>Настоящий <see cref="MaintenanceRunner"/> с восстановителями HKCU на подставном реестре: система не меняется.</summary>
internal static class TestMaintenance
{
    public static MaintenanceRunner Create(IChangeJournal journal, FakeUserRegistry registry, IBoosterConfiguration config, TimeProvider time)
    {
        IActionRestorer[] restorers = [new StartupItemRestorer(registry), new RegistryTweakRestorer(registry, config)];
        return new MaintenanceRunner(
            new ActionExecutor(new ActionPolicy(), journal, time, NullLogger<ActionExecutor>.Instance),
            new RollbackManager(restorers, journal, time, NullLogger<RollbackManager>.Instance),
            new RecoveryValidator(restorers, journal, time, NullLogger<RecoveryValidator>.Instance),
            journal,
            time,
            NullLogger<MaintenanceRunner>.Instance);
    }

    public static ChangeJournal Journal() => new(new InMemoryJournalFileStore(), NullLogger<ChangeJournal>.Instance);
}
