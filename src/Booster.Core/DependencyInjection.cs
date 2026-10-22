using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Drivers;
using Booster.Core.Engine;
using Booster.Core.GameNetwork;
using Booster.Core.Hags;
using Booster.Core.Latency;
using Booster.Core.Profiles;
using Booster.Core.Profiles.Actions;
using Booster.Core.Recovery;
using Booster.Core.Reports;
using Booster.Core.Sensors;
using Booster.Core.SystemHealth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Booster.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddBoosterCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IActionPolicy, ActionPolicy>();
        services.TryAddSingleton<ICapabilityRegistry, CapabilityRegistry>();
        services.TryAddSingleton<SectionRunner>();
        services.TryAddSingleton<SystemDiagnosticsCollector>();
        services.TryAddSingleton<NetworkTester>();
        services.TryAddSingleton<ServerResolver>();
        services.TryAddSingleton<TraceRouteRunner>();
        services.TryAddSingleton<PingRoundRunner>();
        services.TryAddSingleton<ConsumerResolver>();
        services.TryAddSingleton<GameNetworkMeasurer>();
        services.TryAddSingleton<IGameNetworkMeasurer>(sp => sp.GetRequiredService<GameNetworkMeasurer>());
        services.TryAddSingleton<LoadTestRunner>();
        services.TryAddSingleton<ILoadTestRunner>(sp => sp.GetRequiredService<LoadTestRunner>());
        services.TryAddSingleton<IActiveSessionActions, ActiveSessionActions>();
        services.TryAddSingleton<SnapshotCollector>();
        services.TryAddSingleton<OfficialSourceResolver>();
        services.TryAddSingleton<ReportDocumentBuilder>();
        services.TryAddSingleton<IChangeJournal, ChangeJournal>();
        services.TryAddSingleton<RollbackManager>();
        services.TryAddSingleton<RecoveryValidator>();
        services.TryAddSingleton<IProfileRepository, ProfileRepository>();
        services.TryAddSingleton<ActionExecutor>();
        services.TryAddSingleton<IActionCatalog, ActionCatalog>();
        services.TryAddSingleton<SessionFrameCapture>();
        services.TryAddSingleton<GamingProfileManager>();
        services.TryAddSingleton<StartupRecovery>();
        services.TryAddSingleton<MaintenanceRunner>();
        services.TryAddSingleton<GameSessionCoordinator>();
        services.TryAddSingleton<ISensorProvider, SensorProvider>();
        services.TryAddSingleton<SensorRecorder>();
        services.TryAddSingleton<BenchmarkRecorder>();
        services.TryAddSingleton<BenchmarkRunner>();
        services.TryAddSingleton<TraceRecorder>();
        services.TryAddSingleton<HagsExperimentService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActionRestorer, PowerPlanRestorer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActionRestorer, GamePriorityRestorer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActionRestorer, StartupItemRestorer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActionRestorer, MachineStartupItemRestorer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActionRestorer, RegistryTweakRestorer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportExporter, JsonReportExporter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportExporter, HtmlReportExporter>());
        return services;
    }
}
