using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Booster.Core.Profiles;

namespace Booster.Core.SystemHealth;

public sealed class SystemDiagnosticsCollector(IMemoryStatusSource memory, IPagefileSource pagefile, IProcessResourceSource processes,
    IServiceInventorySource services, IStabilityEventSource stability, INetworkAdapterSource network, IStartupSource startup,
    IGamingSettingsSource gaming, IHagsSource hags, IProcessProvider processList, SectionRunner runner, IBoosterConfiguration config, TimeProvider time)
{
    public const string MemorySectionId = "memoryStatus", PagefileSectionId = "pagefile", ProcessMemorySectionId = "processMemory",
        ServicesSectionId = "services", StabilitySectionId = "stability", NetworkSectionId = "networkAdapters",
        StartupSectionId = "startup", GamingSectionId = "gaming", OverlaysSectionId = "overlays", HagsSectionId = "hags";

    private static readonly char[] PathSeparators = ['\\', '/'];

    public async Task<SystemDiagnostics> CollectAsync(CancellationToken ct)
    {
        var settings = config.Current.Settings;
        var detectors = settings.Detectors;
        var diagnostics = settings.Diagnostics;
        var since = time.GetUtcNow() - TimeSpan.FromDays(diagnostics.StabilityDays);

        var memoryTask = RunSync(MemorySectionId, memory.Read, detectors, ct);
        var pagefileTask = runner.RunAsync(PagefileSectionId, pagefile.ReadAsync, detectors.TimeoutFor(PagefileSectionId), ct);
        var processesTask = RunSync(ProcessMemorySectionId, () => processes.ReadMemory(diagnostics.TopProcessCount), detectors, ct);
        var servicesTask = runner.RunAsync(ServicesSectionId, services.ReadAsync, detectors.TimeoutFor(ServicesSectionId), ct);
        var stabilityTask = runner.RunAsync(StabilitySectionId, t => stability.ReadAsync(since, diagnostics.MaxStabilityEvents, t),
            detectors.TimeoutFor(StabilitySectionId), ct);
        var networkTask = RunSync(NetworkSectionId, ReadPhysicalAdapters, detectors, ct);
        var startupTask = RunSync(StartupSectionId, ReadStartup, detectors, ct);
        var gamingTask = RunSync(GamingSectionId, gaming.Read, detectors, ct);
        var overlaysTask = RunSync(OverlaysSectionId, ReadOverlays, detectors, ct);
        var hagsTask = RunSync(HagsSectionId, hags.ReadEnabled, detectors, ct);
        await Task.WhenAll(memoryTask, pagefileTask, processesTask, servicesTask, stabilityTask, networkTask, startupTask, gamingTask, overlaysTask,
            hagsTask).ConfigureAwait(false);

        return new SystemDiagnostics(await memoryTask.ConfigureAwait(false), await pagefileTask.ConfigureAwait(false),
            await processesTask.ConfigureAwait(false), await servicesTask.ConfigureAwait(false), await stabilityTask.ConfigureAwait(false),
            await networkTask.ConfigureAwait(false), await startupTask.ConfigureAwait(false), await gamingTask.ConfigureAwait(false),
            await overlaysTask.ConfigureAwait(false), Inner(await hagsTask.ConfigureAwait(false)));
    }

    private static Reading<T> Inner<T>(Reading<Reading<T>> section) =>
        section.IsAvailable ? section.Value : new Reading<T>(default, section.Status, section.Source, section.Note);

    private Task<Reading<T>> RunSync<T>(string sectionId, Func<T> read, DetectorSettings detectors, CancellationToken ct) =>
        runner.RunAsync(sectionId, t => Task.Run(read, t), detectors.TimeoutFor(sectionId), ct);

    private IReadOnlyList<NetworkAdapterInfo> ReadPhysicalAdapters() =>
        network.Read().Where(a => a.Medium != NetworkMedium.Tunnel).ToArray();

    private IReadOnlyList<StartupItem> ReadStartup()
    {
        var entries = startup.Read();
        Dictionary<string, long>? memoryByName;
        try
        {
            memoryByName = processes.ReadMemory(int.MaxValue)
                .Select(p => (Name: ProcessNames.Normalize(p.Name), p.PrivateBytes))
                .Where(p => p.Name is not null)
                .GroupBy(p => p.Name!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.PrivateBytes), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            memoryByName = null;
        }

        return [.. entries.Select(entry => new StartupItem(entry,
            memoryByName is not null && ExecutableName(entry.ExecutablePath) is { } name && memoryByName.TryGetValue(name, out var bytes)
                ? bytes
                : null))];
    }

    private IReadOnlyList<RunningOverlay> ReadOverlays()
    {
        var running = processList.List()
            .Select(p => (Name: ProcessNames.Normalize(p.Name), p.ProcessId))
            .Where(p => p.Name is not null)
            .ToLookup(p => p.Name!, p => p.ProcessId, StringComparer.OrdinalIgnoreCase);
        return [.. config.Current.Catalog.Overlays
            .Select(overlay => (overlay, ids: ProcessNames.Normalize(overlay.Process) is { } name ? running[name].ToArray() : []))
            .Where(o => o.ids.Length > 0)
            .Select(o => new RunningOverlay(o.overlay.Process, o.overlay.Name, o.ids))];
    }

    // Вне Windows Path.GetFileName не считает «\» разделителем.
    private static string? ExecutableName(string? path) =>
        path is null ? null : ProcessNames.Normalize(path[(path.LastIndexOfAny(PathSeparators) + 1)..]);
}
