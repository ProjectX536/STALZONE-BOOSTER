using Booster.Core.Configuration;

namespace Booster.Core.Tests.TestSupport;

public sealed class FakeConfiguration(
    int defaultTimeoutMs = 15000,
    int driversTimeoutMs = 90000,
    OfficialSources? sources = null,
    string? presentMonPath = null,
    int retainedSessionCount = 50,
    int gameExitConfirmationCount = 2,
    BenchmarkSettings? benchmark = null,
    DiagnosticsSettings? diagnostics = null,
    NetworkSettings? network = null,
    GameNetworkSettings? gameNetwork = null,
    GameNetworkThresholds? gameNetworkThresholds = null,
    IReadOnlyDictionary<string, int>? timeoutsMs = null,
    SystemCatalog? catalog = null) : IBoosterConfiguration
{
    public static readonly GameNetworkThresholds DefaultGameNetworkThresholds =
        new(10, 30, 5, 3, 0.2, 20, [5, 30, 60, 200, 400], 5, 100, -67, 2, 3, 10);

    public static readonly GameNetworkSettings DefaultGameNetwork = new(180, 10, 30, 3, 5, 20, 30, 4096);

    public static readonly Thresholds DefaultThresholds = new(1, 300, 10,
        new ThrottleSettings(2, 90, 10, 10, 30),
        new StutterSettings(2.0, 8, 30),
        new BottleneckSettings(
            GpuBoundFrameShare: 0.9, GpuBusyPercent: 90, GpuIdleFrameShare: 0.75, GpuIdlePercent: 75,
            BusyThreadPercent: 90, SystemCpuPercent: 90, CpuBoundFrameShare: 0.9,
            RefreshTolerancePercent: 3, SyncedFrameShare: 0.9, UniformFrameTimeRatio: 1.15,
            VramUsedPercent: 90, AvailableMemoryPercent: 10, CommitPercent: 90, PageReadsPerSecond: 100,
            DiskBusyPercent: 90, DiskLatencyMs: 50, StuttersPerMinute: 6, PercentileRatio: 2.0,
            DpcTimePercent: 2, InterruptTimePercent: 2),
        new ComparisonSettings(2, 3, 5, 1),
        new RecommendationSettings(5),
        new MemoryThresholds(90),
        new NetworkThresholds(10, 100, 10, 1),
        new StartupThresholds(50),
        DefaultGameNetworkThresholds);

    public static readonly SystemCatalog DefaultCatalog = new(SystemCatalog.CurrentSchemaVersion, new ServiceCatalog(
        SystemCritical:
        [
            "RpcSs", "RpcEptMapper", "DcomLaunch", "PlugPlay", "Power", "EventLog", "EventSystem",
            "wuauserv", "UsoSvc", "WaaSMedicSvc", "BITS", "CryptSvc", "TrustedInstaller",
            "SamSs", "LSM", "ProfSvc", "Schedule", "Winmgmt", "gpsvc",
            "BrokerInfrastructure", "SystemEventsBroker", "CoreMessagingRegistrar", "StateRepository", "UserManager", "Appinfo",
            "AudioSrv", "AudioEndpointBuilder",
            "Dhcp", "Dnscache", "nsi", "NlaSvc", "netprofm", "LanmanWorkstation", "WlanSvc", "Wcmsvc",
            "DeviceInstall", "DsmSvc", "KeyIso", "VaultSvc",
        ],
        Security: ["WinDefend", "WdNisSvc", "Sense", "SecurityHealthService", "wscsvc", "mpssvc", "BFE", "SgrmBroker", "MDCoreSvc"],
        SecurityKeywords:
        [
            "antivirus", "anti-virus", "defender", "firewall", "security", "kaspersky", "eset", "avast", "avg", "bitdefender",
            "norton", "mcafee", "malwarebytes", "sophos", "trend micro", "dr.web", "comodo", "panda",
        ],
        AntiCheat: ["EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "vgc", "FACEIT", "PnkBstrA", "PnkBstrB", "EAAntiCheatService", "npggsvc"],
        AntiCheatKeywords: ["anticheat", "anti-cheat", "battleye", "easyanticheat", "punkbuster", "vanguard", "faceit", "gameguard", "xigncode"],
        DeviceKeywords:
        [
            "driver", "nvidia", "amd", "radeon", "intel", "realtek", "audio", "bluetooth", "wireless", "wi-fi", "ethernet",
            "network", "vpn", "killer", "synaptics", "touchpad", "printer",
        ]),
        Overlays:
        [
            new("RTSS", "RivaTuner Statistics Server"),
            new("gameoverlayui", "Оверлей Steam"),
            new("NVIDIA Overlay", "Оверлей NVIDIA"),
            new("Overwolf", "Overwolf"),
            new("Discord", "Discord"),
        ],
        RegistryTweaks:
        [
            new(RegistryTweakIds.GameMode, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", "1", 19041),
            new(RegistryTweakIds.GpuHighPerformance, @"Software\Microsoft\DirectX\UserGpuPreferences", null, "GpuPreference=2", 17134),
            new(RegistryTweakIds.WindowedFlipModel, @"Software\Microsoft\DirectX\UserGpuPreferences", null, "SwapEffectUpgradeEnable=1", 22621),
        ],
        NetworkConsumers:
        [
            new("steam.exe", null, "Steam", NetworkConsumerKind.Downloader),
            new(null, "DoSvc", "Оптимизация доставки", NetworkConsumerKind.DeliveryOptimization),
            new(null, "wuauserv", "Центр обновления Windows", NetworkConsumerKind.WindowsUpdate),
        ],
        TunnelKeywords: ["WireGuard", "OpenVPN"]);

    public static readonly DiagnosticsSettings DefaultDiagnostics = new(14, 500, 10, 2000);

    public static readonly NetworkSettings DefaultNetwork = new(30, 1000, 1000, null);

    public static readonly BenchmarkSettings DefaultBenchmark = new(3, 30, 10, 10, true, 100, 300);

    public BoosterConfigurationSnapshot Current { get; } = new(
        new BoosterSettings(BoosterSettings.CurrentSchemaVersion,
            new MonitoringSettings(2000, 2000, 2000, 2000, gameExitConfirmationCount),
            new DetectorSettings(defaultTimeoutMs, timeoutsMs ?? new Dictionary<string, int> { ["drivers"] = driversTimeoutMs }),
            new PresentMonSettings(presentMonPath, "BoosterCapture"),
            benchmark ?? DefaultBenchmark,
            new LoggingSettings(14),
            new JournalSettings(retainedSessionCount),
            diagnostics ?? DefaultDiagnostics,
            network ?? DefaultNetwork,
            gameNetwork ?? DefaultGameNetwork),
        gameNetworkThresholds is null ? DefaultThresholds : DefaultThresholds with { GameNetwork = gameNetworkThresholds },
        sources ?? new OfficialSources(OfficialSources.CurrentSchemaVersion, [], [], [], "ms-settings:windowsupdate"),
        catalog ?? DefaultCatalog,
        []);

    public Task SaveUserSettingsAsync(BoosterSettings settings, CancellationToken ct) => throw new NotSupportedException();
}
