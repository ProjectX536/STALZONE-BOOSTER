using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.Drivers;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Booster.Core.Hags;
using Booster.Core.GameNetwork;
using Booster.Core.Latency;
using Booster.Core.Profiles;
using Booster.Core.Recovery;
using Booster.Core.Reports;
using Booster.Core.Sensors;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booster.Core.Tests;

public class DependencyInjectionTests
{
    private sealed class StubHardware : IHardwareProvider
    {
        public Task<HardwareInventory> ScanAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubDrivers : IDriverInventoryProvider
    {
        public Task<DriverInventoryResult> ScanAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubPrivileges : IPrivilegeInfo
    {
        public PrivilegeState GetCurrent() => new(false, false);
    }

    private sealed class StubFrameCapture : IFrameCaptureProvider
    {
        public Task<FrameCaptureAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FrameCaptureResult> CaptureAsync(FrameCaptureRequest request, Action? started, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubDisplays : IInventoryDetector<Reading<IReadOnlyList<DisplayInfo>>>
    {
        public string SectionId => "displays";

        public Task<Reading<IReadOnlyList<DisplayInfo>>> DetectAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Core_services_resolve_as_singletons()
    {
        var services = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IHardwareProvider, StubHardware>()
            .AddSingleton<IDriverInventoryProvider, StubDrivers>()
            .AddSingleton<IFrameCaptureProvider, StubFrameCapture>()
            .AddSingleton<IPrivilegeInfo, StubPrivileges>()
            .AddSingleton<IBoosterConfiguration>(new FakeConfiguration())
            .AddSingleton<IJournalFileStore, InMemoryJournalFileStore>()
            .AddSingleton<IProfileFileSource>(InMemoryProfileFileSource.WithShippedCompetitive())
            .AddSingleton<IProcessProvider, FakeProcessProvider>()
            .AddSingleton<IProcessPriorityController, FakeProcessProvider>()
            .AddSingleton<IAppCloser, FakeAppCloser>()
            .AddSingleton<ISystemLoadSource, FakeSystemLoad>()
            .AddSingleton<IPowerPlanProvider>(new FakePowerPlanProvider())
            .AddSingleton<ICpuSensorSource, FakeCpuSensorSource>()
            .AddSingleton<IProcessActivitySource, FakeProcessActivitySource>()
            .AddSingleton<ISystemPressureSource, FakeSystemPressureSource>()
            .AddSingleton<IInventoryDetector<GpuInventory>>(new FakeGpuDetector())
            .AddSingleton<IInventoryDetector<Reading<IReadOnlyList<DisplayInfo>>>, StubDisplays>()
            .AddSingleton<IGpuProvider>(new FakeGpuProvider(FakeGpuProvider.GenericId))
            .AddSingleton<IMemoryStatusSource>(new FakeMemoryStatusSource())
            .AddSingleton<IPagefileSource>(new FakePagefileSource())
            .AddSingleton<IProcessResourceSource, FakeProcessResourceSource>()
            .AddSingleton<IServiceInventorySource>(new FakeServiceInventorySource())
            .AddSingleton<IStabilityEventSource>(new FakeStabilityEventSource())
            .AddSingleton<INetworkAdapterSource, FakeNetworkAdapters>()
            .AddSingleton<IStartupSource>(new FakeStartupSource())
            .AddSingleton<IGamingSettingsSource>(new FakeGamingSettingsSource())
            .AddSingleton<IHagsSource>(new FakeHagsSource())
            .AddSingleton<IBenchmarkStore>(new FakeBenchmarkStore())
            .AddSingleton<IUserRegistry>(new FakeUserRegistry())
            .AddSingleton<IMachineRegistry>(new FakeUserRegistry())
            .AddSingleton<IPingSource, FakePing>()
            .AddSingleton<ITrafficSource, FakeTrafficSource>()
            .AddSingleton<ITcpConnectionSource, FakeTcpConnections>()
            .AddSingleton<ITraceRouteSource, FakeTraceRoute>()
            .AddSingleton<IRouteSource, FakeRouteSource>()
            .AddSingleton<IWifiSource, FakeWifiSource>()
            .AddSingleton<ISelfCpuSource, FakeSelfCpu>()
            .AddSingleton<IGameNetworkStore, InMemoryGameNetworkStore>()
            .AddSingleton<IWprRunner, FakeWprRunner>()
            .AddSingleton<IStateFileStore, InMemoryStateFileStore>()
            .AddSingleton<IBootTimeSource, FakeBootTimeSource>()
            .AddBoosterCore();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<ActionPolicy>(provider.GetRequiredService<IActionPolicy>());
        Assert.Same(provider.GetRequiredService<ICapabilityRegistry>(), provider.GetRequiredService<ICapabilityRegistry>());
        Assert.NotNull(provider.GetRequiredService<SectionRunner>());
        Assert.Same(provider.GetRequiredService<SnapshotCollector>(), provider.GetRequiredService<SnapshotCollector>());
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
        Assert.Same(provider.GetRequiredService<OfficialSourceResolver>(), provider.GetRequiredService<OfficialSourceResolver>());
        Assert.Same(provider.GetRequiredService<ReportDocumentBuilder>(), provider.GetRequiredService<ReportDocumentBuilder>());
        Assert.Equal(new[] { "json", "html" }, provider.GetServices<IReportExporter>().Select(e => e.Format));
        Assert.IsType<ChangeJournal>(provider.GetRequiredService<IChangeJournal>());
        Assert.Same(provider.GetRequiredService<IChangeJournal>(), provider.GetRequiredService<IChangeJournal>());
        Assert.Same(provider.GetRequiredService<RollbackManager>(), provider.GetRequiredService<RollbackManager>());
        Assert.Same(provider.GetRequiredService<RecoveryValidator>(), provider.GetRequiredService<RecoveryValidator>());
        Assert.IsType<ProfileRepository>(provider.GetRequiredService<IProfileRepository>());
        Assert.Same(provider.GetRequiredService<IProfileRepository>(), provider.GetRequiredService<IProfileRepository>());
        Assert.Same(provider.GetRequiredService<ActionExecutor>(), provider.GetRequiredService<ActionExecutor>());
        Assert.IsType<ActionCatalog>(provider.GetRequiredService<IActionCatalog>());
        Assert.Same(provider.GetRequiredService<GamingProfileManager>(), provider.GetRequiredService<GamingProfileManager>());
        Assert.Same(provider.GetRequiredService<StartupRecovery>(), provider.GetRequiredService<StartupRecovery>());
        Assert.Same(provider.GetRequiredService<GameSessionCoordinator>(), provider.GetRequiredService<GameSessionCoordinator>());
        Assert.Equal([ActionIds.PowerPlan, ActionIds.GamePriority, ActionIds.StartupItem, ActionIds.MachineStartupItem,
                ActionIds.RegistryTweak], provider.GetServices<IActionRestorer>().Select(r => r.ActionId));
        Assert.IsType<SensorProvider>(provider.GetRequiredService<ISensorProvider>());
        Assert.Same(provider.GetRequiredService<ISensorProvider>(), provider.GetRequiredService<ISensorProvider>());
        Assert.Same(provider.GetRequiredService<SensorRecorder>(), provider.GetRequiredService<SensorRecorder>());
        Assert.Same(provider.GetRequiredService<BenchmarkRecorder>(), provider.GetRequiredService<BenchmarkRecorder>());
        Assert.Same(provider.GetRequiredService<BenchmarkRunner>(), provider.GetRequiredService<BenchmarkRunner>());
        Assert.Same(provider.GetRequiredService<SystemDiagnosticsCollector>(), provider.GetRequiredService<SystemDiagnosticsCollector>());
        Assert.Same(provider.GetRequiredService<NetworkTester>(), provider.GetRequiredService<NetworkTester>());
        Assert.Same(provider.GetRequiredService<TraceRecorder>(), provider.GetRequiredService<TraceRecorder>());
        Assert.Same(provider.GetRequiredService<HagsExperimentService>(), provider.GetRequiredService<HagsExperimentService>());
        Assert.Same(provider.GetRequiredService<GameNetworkMeasurer>(), provider.GetRequiredService<GameNetworkMeasurer>());
        Assert.Same(provider.GetRequiredService<LoadTestRunner>(), provider.GetRequiredService<LoadTestRunner>());
        Assert.IsType<ActiveSessionActions>(provider.GetRequiredService<IActiveSessionActions>());
    }

    [Fact]
    public void Restorers_are_not_registered_twice()
    {
        var services = new ServiceCollection()
            .AddSingleton<IPowerPlanProvider>(new FakePowerPlanProvider())
            .AddSingleton<IProcessPriorityController, FakeProcessProvider>()
            .AddSingleton<IProcessProvider, FakeProcessProvider>()
            .AddSingleton<IBoosterConfiguration>(new FakeConfiguration())
            .AddSingleton<IUserRegistry>(new FakeUserRegistry())
            .AddSingleton<IMachineRegistry>(new FakeUserRegistry())
            .AddBoosterCore()
            .AddBoosterCore();

        using var provider = services.BuildServiceProvider();

        Assert.Equal(5, provider.GetServices<IActionRestorer>().Count());
    }

    [Fact]
    public void Report_exporters_are_not_registered_twice()
    {
        var services = new ServiceCollection()
            .AddSingleton<IBoosterConfiguration>(new FakeConfiguration())
            .AddBoosterCore()
            .AddBoosterCore();

        using var provider = services.BuildServiceProvider();

        Assert.Equal(2, provider.GetServices<IReportExporter>().Count());
    }

    [Fact]
    public void Registrations_made_before_are_kept()
    {
        var registry = new CapabilityRegistry();
        var services = new ServiceCollection()
            .AddSingleton<ICapabilityRegistry>(registry)
            .AddBoosterCore();

        using var provider = services.BuildServiceProvider();

        Assert.Same(registry, provider.GetRequiredService<ICapabilityRegistry>());
    }
}
