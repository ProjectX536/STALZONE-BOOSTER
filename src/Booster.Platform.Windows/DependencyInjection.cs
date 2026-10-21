using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;
using Booster.Platform.Windows.Drivers;
using Booster.Platform.Windows.Hardware;
using Booster.Platform.Windows.Monitoring;
using Booster.Platform.Windows.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Booster.Platform.Windows;

public static class PlatformDependencyInjection
{
    /// <summary>
    /// Службы Platform. Регистрации через TryAdd: композиция или тест, зарегистрировавшие свою
    /// реализацию раньше, её сохраняют. Общие службы (<c>SectionRunner</c>, <c>ICapabilityRegistry</c>, <c>TimeProvider</c>)
    /// регистрирует <c>AddBoosterCore</c>, логирование (<c>ILogger&lt;T&gt;</c>) — App.
    /// </summary>
    public static IServiceCollection AddBoosterPlatformWindows(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IAppPaths>(_ => new AppPaths());
        services.TryAddSingleton<IFileProbe, FileProbe>();
        services.TryAddSingleton<IRegistryReader, RegistryReader>();
        services.TryAddSingleton<IUserRegistry, UserRegistry>();
        services.TryAddSingleton<IStartupFolders, StartupFolders>();
        services.TryAddSingleton<IStartupSource, StartupSource>();
        services.TryAddSingleton<IGamingSettingsSource, GamingSettingsSource>();
        services.TryAddSingleton<IHagsSource, HagsSource>();
        services.TryAddSingleton<IPrivilegeInfo, WindowsPrivilegeInfo>();
        services.TryAddSingleton<IConfigFileSource, ConfigFileSource>();
        services.TryAddSingleton<IJournalFileStore, JournalFileStore>();
        services.TryAddSingleton<IStateFileStore, StateFileStore>();
        services.TryAddSingleton<IBootTimeSource, BootTimeSource>();
        services.TryAddSingleton<IBenchmarkStore, BenchmarkFileStore>();
        services.TryAddSingleton<IProfileFileSource, ProfileFileSource>();
        services.TryAddSingleton<IProcessProvider, ProcessProvider>();
        services.TryAddSingleton<IProcessPriorityController, ProcessProvider>();
        services.TryAddSingleton<IAppCloser, ProcessProvider>();
        services.TryAddSingleton<ISystemLoadSource, SystemLoadSource>();
        services.TryAddSingleton<IPowerPlanProvider, PowerPlanProvider>();
        services.TryAddSingleton<IBoosterConfiguration, BoosterConfiguration>();
        services.TryAddSingleton<ICimClient>(sp => new ManagementCimClient(TimeSpan.FromMilliseconds(
            sp.GetRequiredService<IBoosterConfiguration>().Current.Settings.Detectors.DefaultTimeoutMs)));

        services.TryAddSingleton<ICpuTopologySource, CpuTopologySource>();
        services.TryAddSingleton<IFirmwareTypeSource, FirmwareTypeSource>();
        services.TryAddSingleton<IDxgiAdapterSource, DxgiAdapterSource>();
        services.TryAddSingleton<IKmtAdapterTypeSource, KmtAdapterTypeSource>();
        services.TryAddSingleton<IKmtPerfDataSource, KmtPerfDataSource>();
        services.TryAddSingleton<IDisplayConfigSource, DisplayConfigSource>();

        services.TryAddSingleton<IInventoryDetector<CpuInventory>, CpuDetector>();
        services.TryAddSingleton<IInventoryDetector<GpuInventory>, GpuDetector>();
        services.TryAddSingleton<IInventoryDetector<MemoryInventory>, RamDetector>();
        services.TryAddSingleton<IInventoryDetector<SystemBoardInfo>, MotherboardDetector>();
        services.TryAddSingleton<IInventoryDetector<OsInfo>, OsDetector>();
        services.TryAddSingleton<IInventoryDetector<IReadOnlyList<StorageDeviceInfo>>, StorageDetector>();
        services.TryAddSingleton<IInventoryDetector<Reading<IReadOnlyList<DisplayInfo>>>, DisplayDetector>();
        services.TryAddSingleton<IHardwareProvider, HardwareScanner>();

        // Свой клиент: Win32_PnPSignedDriver отдаёт строки медленнее остальных классов, ожидание строки — по таймауту секции драйверов.
        services.TryAddSingleton<IDriverInventoryProvider>(sp => new DriverInventory(new ManagementCimClient(
            sp.GetRequiredService<IBoosterConfiguration>().Current.Settings.Detectors.TimeoutFor(SectionIds.Drivers))));

        services.TryAddSingleton<PresentMonLocator>();
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.TryAddSingleton<IWprRunner, WprRunner>();
        services.TryAddSingleton<IProcessLookup, ProcessLookup>();
        services.TryAddSingleton<IGpuEngineCounterSource, GpuEngineCounterSource>();
        services.TryAddSingleton<IGpuAdapterUsageSampler, GpuAdapterUsageSampler>();
        services.TryAddSingleton<ICpuSensorSource, CpuSensorSource>();
        services.TryAddSingleton<IGpuSystemCounterSource, GpuSystemCounterSource>();
        services.TryAddSingleton<IFrameCaptureProvider, PresentMonProvider>();
        services.TryAddSingleton<SelfUsageMonitor>();
        services.TryAddSingleton<IProcessListSource, ProcessListSource>();
        services.TryAddSingleton<IProcessActivitySource, ProcessActivitySource>();
        services.TryAddSingleton<ISystemPressureSource, SystemPressureSource>();

        services.TryAddSingleton<IMemoryStatusSource, MemoryStatusSource>();
        services.TryAddSingleton<IPagefileSource, PagefileSource>();
        services.TryAddSingleton<IProcessResourceSource, ProcessResourceSource>();
        services.TryAddSingleton<IServiceInventorySource, ServiceInventorySource>();
        services.TryAddSingleton<IStabilityEventSource, StabilityEventSource>();
        services.TryAddSingleton<INetworkAdapterSource, NetworkAdapterSource>();
        services.TryAddSingleton<IPingSource, PingSource>();
        return services;
    }
}
