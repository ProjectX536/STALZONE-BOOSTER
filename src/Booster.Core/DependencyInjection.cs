using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Drivers;
using Booster.Core.Engine;
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
    /// <summary>
    /// Службы Core. Регистрации через TryAdd: композиция или тест, зарегистрировавшие свою реализацию раньше,
    /// её сохраняют. Источники данных (<see cref="IHardwareProvider"/>, <see cref="IDriverInventoryProvider"/>,
    /// <see cref="IPrivilegeInfo"/>, конфигурацию, файлы журнала и профилей, процессы, нагрузку и схемы питания системы, датчики CPU,
    /// детектор GPU и провайдеры GPU, память, подкачку, службы, журнал стабильности и сеть) и логирование подключают Platform,
    /// Providers и App.
    /// </summary>
    public static IServiceCollection AddBoosterCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IActionPolicy, ActionPolicy>();
        services.TryAddSingleton<ICapabilityRegistry, CapabilityRegistry>();
        services.TryAddSingleton<SectionRunner>();
        services.TryAddSingleton<SystemDiagnosticsCollector>();
        services.TryAddSingleton<NetworkTester>();
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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActionRestorer, RegistryTweakRestorer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportExporter, JsonReportExporter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportExporter, HtmlReportExporter>());
        return services;
    }
}
