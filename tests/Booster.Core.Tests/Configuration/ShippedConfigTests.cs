using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Profiles;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.Configuration;

public class ShippedConfigTests
{
    private static string ReadShipped(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config", fileName));

    private static ConfigLoadResult<T> LoadShipped<T>(string fileName) =>
        ConfigLoader.Load<T>(fileName, ReadShipped(fileName), userJson: null, supportedVersion: 1, migrations: []);

    [Fact]
    public void Settings_load_without_warnings_and_need_no_clamping()
    {
        var loaded = LoadShipped<BoosterSettings>("settings.json");
        var normalized = SettingsNormalizer.Normalize(loaded.Value);

        Assert.Empty(loaded.Warnings);
        Assert.Empty(normalized.Warnings);
        Assert.Equal(1, loaded.Value.SchemaVersion);
        Assert.Equal(TimeSpan.FromSeconds(90), loaded.Value.Detectors.TimeoutFor("drivers"));
        Assert.Equal(TimeSpan.FromSeconds(10), loaded.Value.Detectors.TimeoutFor(SystemDiagnosticsCollector.StartupSectionId));
        Assert.Equal(TimeSpan.FromSeconds(5), loaded.Value.Detectors.TimeoutFor(SystemDiagnosticsCollector.GamingSectionId));
        Assert.Equal(TimeSpan.FromSeconds(5), loaded.Value.Detectors.TimeoutFor(SystemDiagnosticsCollector.HagsSectionId));
        Assert.Equal(2000, loaded.Value.Monitoring.GameDetectionIntervalMs);
        Assert.Equal(2000, loaded.Value.Monitoring.SensorIntervalMs);
        Assert.Equal(2, loaded.Value.Monitoring.GameExitConfirmationCount);
        Assert.Equal(50, loaded.Value.Journal.RetainedSessionCount);
        Assert.Equal(new BenchmarkSettings(RunCount: 3, RunSeconds: 30, PauseSeconds: 10, StartDelaySeconds: 10, SoundCues: true,
            RetainedSeriesCount: 100, SessionCaptureSeconds: 300), loaded.Value.Benchmark);
    }

    [Fact]
    public void Competitive_profile_loads_without_warnings_and_needs_no_normalization()
    {
        var loaded = LoadShipped<GamingProfile>(Path.Combine("Profiles", "Competitive.json"));
        var normalized = ProfileNormalizer.Normalize(loaded.Value, "Competitive.json");

        Assert.Empty(loaded.Warnings);
        Assert.Empty(normalized.Warnings);
        Assert.Same(loaded.Value, normalized.Value);
        var profile = loaded.Value;
        Assert.Equal("competitive", profile.ProfileId);
        Assert.Empty(profile.Games);
        Assert.False(profile.AutoStart);
        Assert.Equal(
            [ActionIds.PowerPlan, ActionIds.GamePriority, ActionIds.BackgroundApps, "registry-tweaks", "driver-updates", "pagefile", "windows-services"],
            profile.Actions.Select(a => a.Id));
        Assert.Equal([ProfileFactory.HighPerformance], profile.FindAction(ActionIds.PowerPlan)?.TargetSchemes);
        Assert.Equal(ActionMode.AskFirst, profile.FindAction(ActionIds.PowerPlan)?.Mode);
        Assert.Equal("AboveNormal", profile.FindAction(ActionIds.GamePriority)?.Value);
        Assert.Equal(ActionMode.Optional, profile.FindAction(ActionIds.GamePriority)?.Mode);
        Assert.Equal(ActionMode.AskFirst, profile.FindAction(ActionIds.BackgroundApps)?.Mode);
        Assert.Equal(2000, profile.Monitoring.SampleIntervalMs);
        Assert.Equal(10000, profile.BackgroundApps.CloseTimeoutMs);
        Assert.Equal(new ProfileRecovery(true, true, true, true), profile.Recovery);
    }

    [Fact]
    public void Thresholds_load_without_warnings_and_need_no_clamping()
    {
        var loaded = LoadShipped<Thresholds>("thresholds.json");
        var normalized = ThresholdsNormalizer.Normalize(loaded.Value);

        Assert.Empty(loaded.Warnings);
        Assert.Empty(normalized.Warnings);
        Assert.Equal(300, loaded.Value.MinimumFramesForMetrics);
        Assert.Equal(new StutterSettings(Factor: 2.0, MinimumExcessMs: 8, WindowFrames: 30), loaded.Value.Stutter);
        Assert.Equal(new BottleneckSettings(
            GpuBoundFrameShare: 0.9, GpuBusyPercent: 90, GpuIdleFrameShare: 0.75, GpuIdlePercent: 75,
            BusyThreadPercent: 90, SystemCpuPercent: 90, CpuBoundFrameShare: 0.9,
            RefreshTolerancePercent: 3, SyncedFrameShare: 0.9, UniformFrameTimeRatio: 1.15,
            VramUsedPercent: 90, AvailableMemoryPercent: 10, CommitPercent: 90, PageReadsPerSecond: 100,
            DiskBusyPercent: 90, DiskLatencyMs: 50, StuttersPerMinute: 6, PercentileRatio: 2.0,
            DpcTimePercent: 2, InterruptTimePercent: 2), loaded.Value.Bottleneck);
        Assert.Equal(new ComparisonSettings(MinimumRuns: 2, MinimumChangePercent: 3, RunVariationPercent: 5, MinimumStutterChangePerMinute: 1),
            loaded.Value.Comparison);
        Assert.Equal(new RecommendationSettings(BackgroundCpuPercent: 5), loaded.Value.Recommendations);
    }

    [Fact]
    public void Shipped_thresholds_match_fake_configuration()
    {
        var shipped = LoadShipped<Thresholds>("thresholds.json").Value;
        var fake = new FakeConfiguration().Current.Thresholds;

        Assert.Equal(fake.GameNetwork.BufferbloatGradesMs, shipped.GameNetwork.BufferbloatGradesMs);
        Assert.Equal(WithoutGrades(fake), WithoutGrades(shipped));
    }

    private static Thresholds WithoutGrades(Thresholds t) => t with { GameNetwork = t.GameNetwork with { BufferbloatGradesMs = [] } };

    [Fact]
    public void Shipped_benchmark_settings_match_fake_configuration()
    {
        Assert.Equal(LoadShipped<BoosterSettings>("settings.json").Value.Benchmark, new FakeConfiguration().Current.Settings.Benchmark);
    }

    [Fact]
    public void Shipped_diagnostics_and_network_settings_match_fake_configuration()
    {
        var shipped = LoadShipped<BoosterSettings>("settings.json").Value;
        var fake = new FakeConfiguration().Current.Settings;

        Assert.Equal(new DiagnosticsSettings(StabilityDays: 14, MaxStabilityEvents: 500, TopProcessCount: 10, ActivityIntervalMs: 2000),
            shipped.Diagnostics);
        Assert.Equal(new NetworkSettings(PingCount: 30, PingIntervalMs: 1000, PingTimeoutMs: 1000, Host: null), shipped.Network);
        Assert.Equal(shipped.Diagnostics, fake.Diagnostics);
        Assert.Equal(shipped.Network, fake.Network);
    }

    [Fact]
    public void Shipped_memory_and_network_thresholds_have_documented_values()
    {
        var thresholds = LoadShipped<Thresholds>("thresholds.json").Value;

        Assert.Equal(new MemoryThresholds(CommitPercent: 90), thresholds.Memory);
        Assert.Equal(new NetworkThresholds(GatewayLatencyMs: 10, HostLatencyMs: 100, JitterMs: 10, LossPercent: 1), thresholds.Network);
        Assert.Equal(new StartupThresholds(MinPrivateMemoryMb: 50), thresholds.Startup);
    }

    [Fact]
    public void System_catalog_overlays_and_registry_tweaks_match_fake_configuration()
    {
        var catalog = LoadShipped<SystemCatalog>("system-catalog.json").Value;

        Assert.Equal(FakeConfiguration.DefaultCatalog.Overlays, catalog.Overlays);
        Assert.Equal(FakeConfiguration.DefaultCatalog.RegistryTweaks, catalog.RegistryTweaks);
    }

    [Fact]
    public void Registry_tweaks_are_the_three_whitelisted_and_never_touch_game_dvr()
    {
        var tweaks = LoadShipped<SystemCatalog>("system-catalog.json").Value.RegistryTweaks;

        Assert.Equal([RegistryTweakIds.GameMode, RegistryTweakIds.GpuHighPerformance, RegistryTweakIds.WindowedFlipModel],
            tweaks.Select(t => t.Id));
        Assert.All(tweaks, t =>
        {
            Assert.DoesNotContain("GameDVR", t.SubKey, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("GameConfigStore", t.SubKey, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(@"Software\Microsoft\", t.SubKey, StringComparison.Ordinal);
            Assert.True(t.MinBuild > 0);
        });
        Assert.All(tweaks.Where(t => t.ValueName is null), t => Assert.DoesNotContain(";", t.Value, StringComparison.Ordinal));
    }

    [Fact]
    public void Overlay_processes_have_no_exe_suffix()
    {
        var overlays = LoadShipped<SystemCatalog>("system-catalog.json").Value.Overlays;

        Assert.NotEmpty(overlays);
        Assert.All(overlays, o => Assert.DoesNotContain(".exe", o.Process, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void System_catalog_loads_without_warnings_and_matches_fake_configuration()
    {
        var loaded = LoadShipped<SystemCatalog>("system-catalog.json");
        var expected = FakeConfiguration.DefaultCatalog.Services;
        var services = loaded.Value.Services;

        Assert.Empty(loaded.Warnings);
        Assert.Equal(SystemCatalog.CurrentSchemaVersion, loaded.Value.SchemaVersion);
        Assert.Equal(expected.SystemCritical, services.SystemCritical);
        Assert.Equal(expected.Security, services.Security);
        Assert.Equal(expected.SecurityKeywords, services.SecurityKeywords);
        Assert.Equal(expected.AntiCheat, services.AntiCheat);
        Assert.Equal(expected.AntiCheatKeywords, services.AntiCheatKeywords);
        Assert.Equal(expected.DeviceKeywords, services.DeviceKeywords);
    }

    [Fact]
    public void System_catalog_names_belong_to_one_list_and_keywords_are_lower_case()
    {
        var services = LoadShipped<SystemCatalog>("system-catalog.json").Value.Services;

        var names = services.SystemCritical.Concat(services.Security).Concat(services.AntiCheat).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("RpcSs", services.SystemCritical);
        Assert.Contains("WinDefend", services.Security);
        Assert.Contains("EasyAntiCheat", services.AntiCheat);
        foreach (var keyword in services.SecurityKeywords.Concat(services.AntiCheatKeywords).Concat(services.DeviceKeywords))
            Assert.Equal(keyword.ToLowerInvariant(), keyword);
    }

    [Fact]
    public void Official_sources_load_without_warnings()
    {
        var loaded = LoadShipped<OfficialSources>("official-sources.json");

        Assert.Empty(loaded.Warnings);
        Assert.Equal("ms-settings:windowsupdate", loaded.Value.WindowsUpdateUri);
        Assert.Equal(["10DE", "1002", "8086"], loaded.Value.GpuVendors.Select(r => r.Match));
    }

    [Fact]
    public void Every_vendor_url_is_https_on_an_allowed_host()
    {
        var sources = LoadShipped<OfficialSources>("official-sources.json").Value;

        foreach (var rule in sources.GpuVendors.Concat(sources.BoardVendors))
        {
            var url = new Uri(rule.Url);
            Assert.Equal(Uri.UriSchemeHttps, url.Scheme);
            Assert.Contains(sources.AllowedHosts,
                h => url.Host.Equals(h, StringComparison.OrdinalIgnoreCase)
                     || url.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
            Assert.False(string.IsNullOrWhiteSpace(rule.Title));
        }
    }

    [Fact]
    public void Built_in_config_has_game_network_sections()
    {
        var settings = LoadShipped<BoosterSettings>("settings.json");
        var thresholds = LoadShipped<Thresholds>("thresholds.json");
        var catalog = LoadShipped<SystemCatalog>("system-catalog.json");

        Assert.Empty(settings.Warnings);
        Assert.Empty(SettingsNormalizer.Normalize(settings.Value).Warnings);
        Assert.Empty(thresholds.Warnings);
        Assert.Empty(ThresholdsNormalizer.Normalize(thresholds.Value).Warnings);
        Assert.Empty(catalog.Warnings);
        Assert.Equal(FakeConfiguration.DefaultGameNetwork, settings.Value.GameNetwork);
        Assert.Equal(FakeConfiguration.DefaultGameNetworkThresholds.BufferbloatGradesMs, thresholds.Value.GameNetwork.BufferbloatGradesMs);
        Assert.Equal(FakeConfiguration.DefaultGameNetworkThresholds with { BufferbloatGradesMs = [] },
            thresholds.Value.GameNetwork with { BufferbloatGradesMs = [] });
        Assert.Contains(catalog.Value.NetworkConsumers, c => c.Service == "DoSvc" && c.Kind == NetworkConsumerKind.DeliveryOptimization);
        Assert.Contains(catalog.Value.NetworkConsumers, c => c.Service == "wuauserv" && c.Kind == NetworkConsumerKind.WindowsUpdate);
        Assert.NotEmpty(catalog.Value.TunnelKeywords);
    }

    [Fact]
    public void Network_consumers_identify_by_process_or_service()
    {
        var consumers = LoadShipped<SystemCatalog>("system-catalog.json").Value.NetworkConsumers;

        Assert.All(consumers, c =>
        {
            Assert.True((c.Process is null) != (c.Service is null));
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
        });
        Assert.All(consumers.Where(c => c.Process is not null), c => Assert.EndsWith(".exe", c.Process, StringComparison.OrdinalIgnoreCase));
    }
}
