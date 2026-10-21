using Booster.Core;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Drivers;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Booster.Core.Profiles;
using Booster.Core.SystemHealth;
using Booster.Platform.Windows.Common;
using Booster.Platform.Windows.Drivers;
using Booster.Platform.Windows.Hardware;
using Booster.Platform.Windows.Monitoring;
using Booster.Platform.Windows.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booster.Platform.Windows.Tests;

public sealed class DependencyInjectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Booster.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Common_services_resolve_and_keep_registrations_made_before()
    {
        // Чтобы тест не создавал каталоги в настоящем %LOCALAPPDATA%.
        var paths = new AppPaths(Path.Combine(_root, "data"), AppContext.BaseDirectory);
        var services = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IAppPaths>(paths)
            .AddBoosterCore()
            .AddBoosterPlatformWindows();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.Same(paths, provider.GetRequiredService<IAppPaths>());
        Assert.IsType<ManagementCimClient>(provider.GetRequiredService<ICimClient>());
        Assert.IsType<RegistryReader>(provider.GetRequiredService<IRegistryReader>());
        Assert.IsType<UserRegistry>(provider.GetRequiredService<IUserRegistry>());
        Assert.IsType<MachineRegistry>(provider.GetRequiredService<IMachineRegistry>());
        Assert.IsType<StartupSource>(provider.GetRequiredService<IStartupSource>());
        Assert.IsType<GamingSettingsSource>(provider.GetRequiredService<IGamingSettingsSource>());
        Assert.IsType<HagsSource>(provider.GetRequiredService<IHagsSource>());
        Assert.IsType<WindowsPrivilegeInfo>(provider.GetRequiredService<IPrivilegeInfo>());
        Assert.IsType<FileProbe>(provider.GetRequiredService<IFileProbe>());
        Assert.Equal(300, provider.GetRequiredService<IBoosterConfiguration>().Current.Thresholds.MinimumFramesForMetrics);
        Assert.NotNull(provider.GetRequiredService<SectionRunner>());
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
        Assert.IsType<HardwareScanner>(provider.GetRequiredService<IHardwareProvider>());
        Assert.IsType<CpuDetector>(provider.GetRequiredService<IInventoryDetector<CpuInventory>>());
        Assert.IsType<GpuDetector>(provider.GetRequiredService<IInventoryDetector<GpuInventory>>());
        Assert.IsType<RamDetector>(provider.GetRequiredService<IInventoryDetector<MemoryInventory>>());
        Assert.IsType<MotherboardDetector>(provider.GetRequiredService<IInventoryDetector<SystemBoardInfo>>());
        Assert.IsType<OsDetector>(provider.GetRequiredService<IInventoryDetector<OsInfo>>());
        Assert.IsType<StorageDetector>(provider.GetRequiredService<IInventoryDetector<IReadOnlyList<StorageDeviceInfo>>>());
        Assert.IsType<DisplayDetector>(provider.GetRequiredService<IInventoryDetector<Reading<IReadOnlyList<DisplayInfo>>>>());
        Assert.IsType<DriverInventory>(provider.GetRequiredService<IDriverInventoryProvider>());
        Assert.NotNull(provider.GetRequiredService<SnapshotCollector>());
        Assert.NotNull(provider.GetRequiredService<OfficialSourceResolver>());
        Assert.IsType<PresentMonProvider>(provider.GetRequiredService<IFrameCaptureProvider>());
        Assert.NotNull(provider.GetRequiredService<PresentMonLocator>());
        Assert.IsType<ProcessRunner>(provider.GetRequiredService<IProcessRunner>());
        Assert.IsType<ProcessLookup>(provider.GetRequiredService<IProcessLookup>());
        Assert.IsType<GpuEngineCounterSource>(provider.GetRequiredService<IGpuEngineCounterSource>());
        Assert.IsType<GpuAdapterUsageSampler>(provider.GetRequiredService<IGpuAdapterUsageSampler>());
        Assert.IsType<CpuSensorSource>(provider.GetRequiredService<ICpuSensorSource>());
        Assert.IsType<GpuSystemCounterSource>(provider.GetRequiredService<IGpuSystemCounterSource>());
        Assert.IsType<KmtPerfDataSource>(provider.GetRequiredService<IKmtPerfDataSource>());
        Assert.NotNull(provider.GetRequiredService<SelfUsageMonitor>());
        Assert.IsType<ProcessListSource>(provider.GetRequiredService<IProcessListSource>());
        Assert.IsType<JournalFileStore>(provider.GetRequiredService<IJournalFileStore>());
        Assert.IsType<StateFileStore>(provider.GetRequiredService<IStateFileStore>());
        Assert.IsType<BootTimeSource>(provider.GetRequiredService<IBootTimeSource>());
        Assert.IsType<WprRunner>(provider.GetRequiredService<IWprRunner>());
        Assert.IsType<BenchmarkFileStore>(provider.GetRequiredService<IBenchmarkStore>());
        Assert.IsType<ProfileFileSource>(provider.GetRequiredService<IProfileFileSource>());
        Assert.NotNull(provider.GetRequiredService<IProfileRepository>().Current.Find("competitive"));
        Assert.IsType<ProcessProvider>(provider.GetRequiredService<IProcessProvider>());
        Assert.IsType<SystemLoadSource>(provider.GetRequiredService<ISystemLoadSource>());
        Assert.IsType<PowerPlanProvider>(provider.GetRequiredService<IPowerPlanProvider>());
        Assert.IsType<ProcessProvider>(provider.GetRequiredService<IProcessPriorityController>());
        Assert.IsType<ProcessProvider>(provider.GetRequiredService<IAppCloser>());
        Assert.NotNull(provider.GetRequiredService<GamingProfileManager>());
        Assert.IsType<MemoryStatusSource>(provider.GetRequiredService<IMemoryStatusSource>());
        Assert.IsType<PagefileSource>(provider.GetRequiredService<IPagefileSource>());
        Assert.IsType<ProcessResourceSource>(provider.GetRequiredService<IProcessResourceSource>());
        Assert.IsType<ServiceInventorySource>(provider.GetRequiredService<IServiceInventorySource>());
        Assert.IsType<StabilityEventSource>(provider.GetRequiredService<IStabilityEventSource>());
        Assert.IsType<NetworkAdapterSource>(provider.GetRequiredService<INetworkAdapterSource>());
        Assert.IsType<PingSource>(provider.GetRequiredService<IPingSource>());
        Assert.NotNull(provider.GetRequiredService<SystemDiagnosticsCollector>());
        Assert.NotNull(provider.GetRequiredService<NetworkTester>());
    }

    [Fact]
    public void Driver_provider_registered_before_is_kept()
    {
        var stub = new StubDrivers();
        var services = new ServiceCollection()
            .AddSingleton<IDriverInventoryProvider>(stub)
            .AddBoosterPlatformWindows();

        using var provider = services.BuildServiceProvider();

        Assert.Same(stub, provider.GetRequiredService<IDriverInventoryProvider>());
    }

    private sealed class StubDrivers : IDriverInventoryProvider
    {
        public Task<DriverInventoryResult> ScanAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
