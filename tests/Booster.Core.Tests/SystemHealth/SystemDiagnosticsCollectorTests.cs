using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace Booster.Core.Tests.SystemHealth;

public class SystemDiagnosticsCollectorTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeProcessResourceSource _processes = new();
    private readonly FakeStabilityEventSource _stability = new();
    private readonly FakeNetworkAdapters _network = new();
    private FakeStartupSource _startup = new();
    private readonly FakeGamingSettingsSource _gaming = new();
    private readonly FakeHagsSource _hags = new();
    private readonly FakeProcessProvider _processList = new();

    private const long MiB = 1L << 20;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SystemDiagnosticsCollector Collector(IPagefileSource? pagefile = null, FakeConfiguration? config = null) => new(
        new FakeMemoryStatusSource(), pagefile ?? new FakePagefileSource(), _processes, new FakeServiceInventorySource(), _stability, _network,
        _startup, _gaming, _hags, _processList, new SectionRunner(_time, new ListLogger<SectionRunner>()), config ?? new FakeConfiguration(), _time);

    private static StartupEntry Entry(string name, string? executable) =>
        new(name, StartupLocation.UserRun, executable ?? name, executable, StartupState.Enabled);

    private static ProcessMemoryUsage Process(string name, int id, long privateBytes) => new(name, id, privateBytes, privateBytes);

    [Fact]
    public async Task All_sections_are_collected()
    {
        _network.Adapters.Add(FakeNetworkAdapters.Adapter(gateways: SystemSamples.Gateway));

        var system = await Collector().CollectAsync(Ct);

        Assert.Equal(SystemSamples.Memory(), system.Memory.Value);
        Assert.Equal(SystemDiagnosticsCollector.MemorySectionId, system.Memory.Source);
        Assert.True(system.Pagefile.Value!.AutomaticManaged.Value);
        Assert.Equal(SystemSamples.Processes, system.TopMemoryProcesses.Value);
        Assert.Contains(FakeConfiguration.DefaultDiagnostics.TopProcessCount, _processes.MemoryTopCounts);
        Assert.Equal(["RpcSs", "Dnscache", "Spooler"], system.Services.Value!.Select(s => s.Name));
        Assert.Empty(system.Stability.Value!.Events);
        Assert.Equal("eth0", Assert.Single(system.NetworkAdapters.Value!).Id);
        Assert.Empty(system.Startup.Value!);
        Assert.Equal(SystemDiagnosticsCollector.StartupSectionId, system.Startup.Source);
        Assert.Equal(_gaming.Settings, system.Gaming.Value);
        Assert.Equal(SystemDiagnosticsCollector.GamingSectionId, system.Gaming.Source);
        Assert.Empty(system.Overlays.Value!);
        Assert.Equal(SystemDiagnosticsCollector.OverlaysSectionId, system.Overlays.Source);
    }

    [Fact]
    public async Task Startup_items_get_private_memory_of_matching_processes()
    {
        _processes.Memory = () =>
        [
            Process("chrome.exe", 1, 900 * MiB),
            Process("Discord.exe", 2, 300 * MiB),
            Process("discord.exe", 3, 200 * MiB),
        ];
        _startup = new FakeStartupSource(
            Entry("Discord", @"C:\Users\user\AppData\Local\Discord\app-1.0\Discord.exe"),
            Entry("Steam", @"C:\Program Files (x86)\Steam\steam.exe"),
            Entry("Script", null));

        var system = await Collector().CollectAsync(Ct);

        Assert.Equal(["Discord", "Steam", "Script"], system.Startup.Value!.Select(i => i.Entry.Name));
        Assert.Equal([500 * MiB, null, null], system.Startup.Value!.Select(i => i.PrivateBytes));
        // Память считается по всем процессам, а не только по первым topProcessCount.
        Assert.Contains(int.MaxValue, _processes.MemoryTopCounts);
    }

    [Fact]
    public async Task Process_match_is_exact_file_name()
    {
        _processes.Memory = () => [Process("DiscordPTB.exe", 1, 300 * MiB), Process("Update.exe", 2, 10 * MiB)];
        _startup = new FakeStartupSource(
            Entry("Discord", @"C:\Discord\Discord.exe"),
            Entry("DiscordPTB", "C:/Discord/DiscordPTB.exe"),
            Entry("Updater", @"C:\Discord\Update"));

        var system = await Collector().CollectAsync(Ct);

        Assert.Equal([null, 300 * MiB, 10 * MiB], system.Startup.Value!.Select(i => i.PrivateBytes));
    }

    [Fact]
    public async Task Startup_survives_process_read_failure()
    {
        _processes.Memory = () => throw new InvalidDataException("process snapshot failed");
        _startup = new FakeStartupSource(Entry("Discord", @"C:\Discord\Discord.exe"));

        var system = await Collector().CollectAsync(Ct);

        Assert.Equal(DataStatus.Failed, system.TopMemoryProcesses.Status);
        Assert.Equal(DataStatus.Available, system.Startup.Status);
        Assert.Null(Assert.Single(system.Startup.Value!).PrivateBytes);
    }

    [Fact]
    public async Task Startup_source_failure_fails_only_its_section()
    {
        _startup = new FakeStartupSource { Failure = new IOException("No startup location could be read") };

        var system = await Collector().CollectAsync(Ct);

        Assert.Equal(DataStatus.Failed, system.Startup.Status);
        Assert.Equal(DataStatus.Available, system.TopMemoryProcesses.Status);
    }

    [Fact]
    public async Task Failing_source_gives_failed_section_and_others_survive()
    {
        var pagefile = new FakePagefileSource(_ => throw new InvalidOperationException("WMI broken"));

        var system = await Collector(pagefile).CollectAsync(Ct);

        Assert.Equal(DataStatus.Failed, system.Pagefile.Status);
        Assert.Contains("WMI broken", system.Pagefile.Note, StringComparison.Ordinal);
        Assert.Equal(DataStatus.Available, system.Services.Status);
        Assert.Equal(DataStatus.Available, system.Memory.Status);
    }

    [Fact]
    public async Task Overlays_match_catalog_processes_case_insensitively()
    {
        _processList.Start("rtss.exe", 10);
        _processList.Start("Discord.exe", 11);
        _processList.Start("DISCORD", 12);
        _processList.Start("DiscordPTB.exe", 13);
        _processList.Start("chrome.exe", 14);

        var system = await Collector().CollectAsync(Ct);

        Assert.Equal(
            [("RTSS", "RivaTuner Statistics Server", "10"), ("Discord", "Discord", "11,12")],
            system.Overlays.Value!.Select(o => (o.Process, o.Name, string.Join(",", o.ProcessIds.Order()))));
    }

    [Fact]
    public async Task Gaming_source_failure_fails_only_its_section()
    {
        _gaming.Failure = new UnauthorizedAccessException("denied");

        var system = await Collector().CollectAsync(Ct);

        Assert.Equal(DataStatus.Failed, system.Gaming.Status);
        Assert.Equal(DataStatus.Available, system.Overlays.Status);
    }

    [Fact]
    public async Task Hags_section_is_collected()
    {
        _hags.Enabled = Reading<bool>.Known(false, SystemSamples.Source);

        var system = await Collector().CollectAsync(Ct);

        Assert.Equal(Reading<bool>.Known(false, SystemSamples.Source), system.Hags);
    }

    [Fact]
    public async Task Hags_source_failure_fails_only_its_section()
    {
        _hags.Failure = new UnauthorizedAccessException("denied");

        var system = await Collector().CollectAsync(Ct);

        Assert.Equal(DataStatus.Failed, system.Hags.Status);
        Assert.Equal(SystemDiagnosticsCollector.HagsSectionId, system.Hags.Source);
        Assert.Contains("denied", system.Hags.Note, StringComparison.Ordinal);
        Assert.Equal(DataStatus.Available, system.Gaming.Status);
    }

    [Fact]
    public async Task Hags_timeout_comes_from_settings()
    {
        using var block = new ManualResetEventSlim();
        _hags.Block = block;
        var config = new FakeConfiguration(timeoutsMs: new Dictionary<string, int> { [SystemDiagnosticsCollector.HagsSectionId] = 2000 });
        try
        {
            // Таймеры секций заводятся при вызове; остальные секции ждут 15 с по умолчанию и не истекают.
            var run = Collector(config: config).CollectAsync(Ct);
            _time.Advance(TimeSpan.FromMilliseconds(2000));
            var system = await run;

            Assert.Equal(Reading<bool>.Failure(SystemDiagnosticsCollector.HagsSectionId, "timeout after 2000 ms"), system.Hags);
            Assert.Equal(DataStatus.Available, system.Gaming.Status);
        }
        finally
        {
            block.Set();
        }
    }

    [Fact]
    public async Task Stability_reads_configured_days_and_limit()
    {
        await Collector().CollectAsync(Ct);

        Assert.Equal(_time.GetUtcNow() - TimeSpan.FromDays(14), _stability.LastSince);
        Assert.Equal(500, _stability.LastMaxEvents);
    }
}
