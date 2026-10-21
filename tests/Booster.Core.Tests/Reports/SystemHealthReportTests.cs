using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.Drivers;
using Booster.Core.Hags;
using Booster.Core.Inventory;
using Booster.Core.Reports;
using Booster.Core.Resources;
using Booster.Core.Sensors;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;
using static Booster.Core.Tests.TestSupport.SnapshotFactory;

namespace Booster.Core.Tests.Reports;

public class SystemHealthReportTests
{
    private const long GiB = 1L << 30;
    private const long MiB = 1L << 20;

    private static readonly CultureInfo Russian = new("ru-RU");
    private static readonly ServiceCatalog Catalog = FakeConfiguration.DefaultCatalog.Services;
    private static readonly StartupThresholds StartupThresholds = FakeConfiguration.DefaultThresholds.Startup;

    private static ReportSection Build(SystemReport report, string id) =>
        Assert.Single(SystemHealthReport.Build(report, Catalog, StartupThresholds, Russian), s => s.Id == id);

    private static SystemReport With(Func<SystemDiagnostics, SystemDiagnostics> change, IReadOnlyList<SystemAdvice>? advice = null,
        NetworkTestResult? network = null, IReadOnlyList<TweakProposal>? tweaks = null)
    {
        var healthy = Healthy();
        return Report(healthy with { System = change(healthy.System) }, advice: advice, network: network, tweaks: tweaks);
    }

    private static ReportCell Field(ReportSection section, string label) =>
        Assert.Single(section.Blocks.OfType<ReportFieldList>().SelectMany(l => l.Fields), f => f.Label == label).Value;

    private static IEnumerable<string> Paragraphs(ReportSection section) =>
        section.Blocks.OfType<ReportParagraph>().Select(p => p.Content.Text);

    private static ReportTable Table(ReportSection section, string firstHeader) =>
        Assert.Single(section.Blocks.OfType<ReportTable>(), t => t.Headers[0] == firstHeader);

    private static ReportTable AdviceTable(ReportSection section) => Table(section, ReportStrings.ColumnRecommendation);

    private static SystemReport WithHags(Reading<bool> current, HagsExperimentView? view)
    {
        var healthy = Healthy();
        return Report(healthy with { System = healthy.System with { Hags = current } }, hags: view);
    }

    private static NetworkTestResult NetworkTest(params NetworkTargetResult[] targets) =>
        new(TakenAt, TimeSpan.FromSeconds(10), FakeNetworkAdapters.Adapter("eth0", NetworkMedium.Ethernet, 1_000_000_000, SystemSamples.Gateway),
            targets, Reading<double>.Known(12.5, "test"));

    [Fact]
    public void Sections_follow_drivers_in_document()
    {
        var configuration = new FakeConfiguration();
        var builder = new ReportDocumentBuilder(new OfficialSourceResolver(configuration), configuration);

        var ids = builder.Build(Report(), Russian).Sections.Select(s => s.Id).ToList();

        Assert.Equal([ReportSectionIds.SystemMemory, ReportSectionIds.Services, ReportSectionIds.Stability, ReportSectionIds.Network,
            ReportSectionIds.Startup, ReportSectionIds.Tweaks, ReportSectionIds.Latency, ReportSectionIds.Hags],
            ids.Skip(ids.IndexOf(ReportSectionIds.Drivers) + 1).Take(8));
    }

    [Fact]
    public void Memory_section_shows_commit_percent_and_pagefile_mode()
    {
        var section = Build(Report(), ReportSectionIds.SystemMemory);

        Assert.Equal(DataStatus.Available, section.Status);
        Assert.Equal(string.Format(Russian, ReportStrings.SystemCommitFormat, ByteSizeFormatter.Format(16 * GiB, Russian),
            ByteSizeFormatter.Format(40 * GiB, Russian), 0.4.ToString("P0", Russian)), Field(section, ReportStrings.SystemCommit).Text);
        Assert.Equal(EnumText.Format(PagefileMode.SystemManaged), Field(section, ReportStrings.SystemPagefileMode).Text);
        var processes = Table(section, ReportStrings.ColumnProcess);
        Assert.Equal(["chrome.exe", "4242", ByteSizeFormatter.Format(700 * MiB, Russian), ByteSizeFormatter.Format(900 * MiB, Russian)],
            processes.Rows[0].Cells.Select(c => c.Text));
    }

    [Fact]
    public void Pagefile_table_shows_configured_size_and_free_space()
    {
        var pagefile = new PagefileConfiguration(Reading<bool>.Known(false, "test"),
            [new PagefileSetting(@"C:\pagefile.sys", 4096, 8192), new PagefileSetting(@"D:\pagefile.sys", 0, 0)],
            [new PagefileUsage(@"C:\pagefile.sys", 4096, 512, 1024)]);

        var section = Build(With(s => s with { Pagefile = Reading<PagefileConfiguration>.Known(pagefile, "test") }), ReportSectionIds.SystemMemory);

        var table = Table(section, ReportStrings.ColumnPath);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(
        [
            @"C:\pagefile.sys",
            string.Format(Russian, ReportStrings.PagefileSizeRangeFormat, "4096", "8192"),
            ByteSizeFormatter.Format(4096 * MiB, Russian),
            ByteSizeFormatter.Format(512 * MiB, Russian),
            ByteSizeFormatter.Format(1024 * MiB, Russian),
            string.Format(Russian, ReportStrings.FreeSpaceFormat, ByteSizeFormatter.Format(250 * GiB, Russian), 0.5.ToString("P0", Russian)),
        ], table.Rows[0].Cells.Select(c => c.Text));
        var missing = table.Rows[1].Cells;
        Assert.Equal(ReportStrings.PagefileSizeBySystem, missing[1].Text);
        Assert.Equal(ReportStrings.PagefileNoFile, missing[2].Text);
        Assert.Equal(CellTone.Unavailable, missing[5].Tone);
    }

    [Fact]
    public void Healthy_system_has_no_advice_paragraph()
    {
        var report = Report();

        foreach (var id in new[] { ReportSectionIds.SystemMemory, ReportSectionIds.Services, ReportSectionIds.Stability })
        {
            var section = Build(report, id);
            Assert.Contains(ReportStrings.AdviceNone, Paragraphs(section));
            Assert.DoesNotContain(section.Blocks.OfType<ReportTable>(), t => t.Headers[0] == ReportStrings.ColumnRecommendation);
        }
    }

    [Fact]
    public void Advice_row_has_text_action_risk_and_level()
    {
        SystemAdvice[] advice =
        [
            new(AdviceId.NetworkHighLatency, AdviceModule.Network, FindingSeverity.Warning, SafetyLevel.L3ManualOnly, [SystemSamples.Gateway, "30"]),
            new(AdviceId.NetworkWireless, AdviceModule.Network, FindingSeverity.Info, SafetyLevel.L3ManualOnly, []),
        ];
        var report = Report(advice: advice, network: NetworkTest());

        var rows = AdviceTable(Build(report, ReportSectionIds.Network)).Rows;

        Assert.Equal(2, rows.Count);
        var text = string.Format(Russian, ReportStrings.AdviceText_NetworkHighLatency, SystemSamples.Gateway, "30");
        Assert.Equal(
        [
            string.Format(Russian, ReportStrings.AdviceCellFormat, ReportStrings.AdviceId_NetworkHighLatency, text),
            ReportStrings.AdviceAction_NetworkHighLatency,
            ReportStrings.AdviceRisk_NetworkHighLatency,
            EnumText.Format(SafetyLevel.L3ManualOnly),
        ], rows[0].Cells.Select(c => c.Text));
        Assert.Equal(CellTone.Warning, rows[0].Cells[0].Tone);
        Assert.Equal(CellTone.Info, rows[1].Cells[0].Tone);
        Assert.Contains(ReportStrings.AdviceNone, Paragraphs(Build(report, ReportSectionIds.SystemMemory)));
    }

    [Fact]
    public void Services_table_flags_review_candidates_and_hides_microsoft()
    {
        ServiceInfo[] services =
        [
            SystemSamples.Service("RpcSs"),
            SystemSamples.Service("Spooler"),
            SystemSamples.Service("VendorHelper", "Vendor Inc.") with { StartMode = ServiceStartMode.Manual },
            SystemSamples.Service("VendorUpdater", "Vendor Inc."),
            SystemSamples.Service("WinDefend"),
        ];

        var section = Build(With(s => s with { Services = Reading<IReadOnlyList<ServiceInfo>>.Known(services, "test") }),
            ReportSectionIds.Services);

        var table = Table(section, ReportStrings.ColumnServiceName);
        Assert.Equal(["VendorUpdater", "WinDefend", "VendorHelper"], table.Rows.Select(r => r.Cells[0].Text));
        Assert.Equal([true, false, false], table.Rows.Select(r => r.Flagged));
        Assert.Equal(EnumText.Format(ServiceCategory.Security), table.Rows[1].Cells[3].Text);
        Assert.Equal("5", Field(section, ReportStrings.ServicesTotal).Text);
        Assert.Equal("2", Field(section, EnumText.Format(ServiceCategory.ThirdParty)).Text);
        Assert.Contains(ReportStrings.ServicesCaveat, Paragraphs(section));
    }

    [Fact]
    public void Stability_truncated_note()
    {
        var log = new StabilityLog(TakenAt.AddDays(-14),
        [
            new StabilityEvent(StabilityEventKind.ApplicationCrash, TakenAt.AddDays(-1), "Application Error", 1000, "cs2.exe"),
            new StabilityEvent(StabilityEventKind.ApplicationCrash, TakenAt.AddDays(-3), "Application Error", 1000, "cs2.exe"),
        ], Truncated: true);

        var section = Build(With(s => s with { Stability = Reading<StabilityLog>.Known(log, "test") }), ReportSectionIds.Stability);

        var row = Assert.Single(Table(section, ReportStrings.ColumnEventKind).Rows);
        Assert.Equal([EnumText.Format(StabilityEventKind.ApplicationCrash), "2", "cs2.exe"],
            new[] { row.Cells[0], row.Cells[1], row.Cells[3] }.Select(c => c.Text));
        var note = Assert.Single(section.Blocks.OfType<ReportParagraph>(), p => p.Content.Tone == CellTone.Note);
        Assert.Equal(string.Format(Russian, ReportStrings.StabilityTruncatedFormat, 2), note.Content.Text);
    }

    [Fact]
    public void Network_without_test_shows_hint_and_caveat()
    {
        var section = Build(Report(), ReportSectionIds.Network);

        Assert.Equal(DataStatus.Available, section.Status);
        Assert.Equal(["eth0 name", EnumText.Format(NetworkMedium.Ethernet), string.Format(Russian, ReportStrings.UnitGbps, "1"), SystemSamples.Gateway],
            Assert.Single(Table(section, ReportStrings.ColumnNetworkAdapter).Rows).Cells.Select(c => c.Text));
        Assert.Contains(ReportStrings.NetworkNotTested, Paragraphs(section));
        Assert.Contains(ReportStrings.NetworkCaveat, Paragraphs(section));
        Assert.Contains(ReportStrings.AdviceNotAssessed, Paragraphs(section));
    }

    [Fact]
    public void Network_test_targets_are_listed()
    {
        var gateway = NetworkAnalyzer.Summarize(NetworkTargetKind.Gateway, SystemSamples.Gateway, [1.0, 2.0, 1.0, 2.0]);
        var host = NetworkAnalyzer.Summarize(NetworkTargetKind.Host, "1.1.1.1", [null, null]);

        var section = Build(Report(network: NetworkTest(gateway, host)), ReportSectionIds.Network);

        var table = Table(section, ReportStrings.ColumnTarget);
        Assert.Equal(
        [
            EnumText.Format(NetworkTargetKind.Gateway), SystemSamples.Gateway,
            string.Format(Russian, ReportStrings.UnitMs, 1.5.ToString("0.#", Russian)),
            string.Format(Russian, ReportStrings.UnitMs, 1.0.ToString("0.#", Russian)),
            SensorFieldFormatter.Percent(0, Russian),
            string.Format(Russian, ReportStrings.NetworkRepliesFormat, 4, 4),
        ], table.Rows[0].Cells.Select(c => c.Text));
        Assert.Equal(CellTone.Unavailable, table.Rows[1].Cells[2].Tone);
        Assert.Equal(string.Format(Russian, ReportStrings.NetworkRepliesFormat, 0, 2), table.Rows[1].Cells[5].Text);
        Assert.Equal("eth0 name", Field(section, ReportStrings.NetworkTestAdapter).Text);
        Assert.Equal(SensorFieldFormatter.Percent(12.5, Russian), Field(section, ReportStrings.NetworkUtilization).Text);
        Assert.DoesNotContain(ReportStrings.NetworkNotTested, Paragraphs(section));
        Assert.Contains(ReportStrings.AdviceNone, Paragraphs(section));
    }

    [Fact]
    public void Unknown_values_are_marked_unavailable()
    {
        var section = Build(With(s => s with { Memory = Reading<MemoryStatus>.Failure("GlobalMemoryStatusEx", "call failed") }),
            ReportSectionIds.SystemMemory);

        Assert.Equal(DataStatus.Failed, section.Status);
        Assert.Equal(CellTone.Unavailable, Field(section, ReportStrings.SystemCommit).Tone);
        Assert.Equal(CellTone.Unavailable, Field(section, ReportStrings.SystemPhysicalMemory).Tone);
        Assert.Contains(ReportStrings.AdviceNotAssessed, Paragraphs(section));
    }

    [Fact]
    public void Sections_follow_order_memory_services_stability_network_startup_tweaks_latency_hags()
    {
        var sections = SystemHealthReport.Build(Report(), Catalog, StartupThresholds, Russian);

        Assert.Equal([ReportSectionIds.SystemMemory, ReportSectionIds.Services, ReportSectionIds.Stability, ReportSectionIds.Network,
            ReportSectionIds.Startup, ReportSectionIds.Tweaks, ReportSectionIds.Latency, ReportSectionIds.Hags], sections.Select(s => s.Id));
        Assert.Equal([EnumText.Format(AdviceModule.Startup), EnumText.Format(AdviceModule.Tweaks), EnumText.Format(AdviceModule.Latency),
            ReportStrings.SectionHags], sections.TakeLast(4).Select(s => s.Title));
    }

    [Fact]
    public void Startup_section_lists_entries_with_verdict()
    {
        const string discordPath = @"C:\Users\Player\AppData\Local\Discord\Discord.exe";
        StartupItem[] items =
        [
            new(new StartupEntry("Tray", StartupLocation.UserFolder, @"C:\Users\Player\Tray.lnk", @"C:\Tools\Tray.exe", StartupState.Enabled), null),
            new(new StartupEntry("Setup", StartupLocation.UserRunOnce, "setup.cmd /resume", null, StartupState.Enabled), null),
            new(new StartupEntry("Discord", StartupLocation.UserRun, $"\"{discordPath}\" --tray", discordPath, StartupState.Enabled), 300 * MiB),
        ];
        var report = With(s => s with
        {
            Startup = Known<IReadOnlyList<StartupItem>>(items),
            Overlays = Known<IReadOnlyList<RunningOverlay>>([new RunningOverlay("Discord.exe", "Discord", [4242, 4243])]),
        });

        var section = Build(report, ReportSectionIds.Startup);

        Assert.Equal(DataStatus.Available, section.Status);
        var table = Table(section, ReportStrings.ColumnStartupItem);
        Assert.Equal(["Discord", "Tray", "Setup"], table.Rows.Select(r => r.Cells[0].Text));
        Assert.Equal(
        [
            "Discord", ReportStrings.StartupLocation_UserRun, ReportStrings.StartupState_Enabled, discordPath,
            ByteSizeFormatter.Format(300 * MiB, Russian), ReportStrings.StartupVerdict_Candidate,
        ], table.Rows[0].Cells.Select(c => c.Text));
        Assert.Equal($"\"{discordPath}\" --tray", table.Rows[0].Cells[0].Tooltip);
        Assert.True(table.Rows[0].Flagged);
        Assert.Equal([ReportStrings.StartupNotRunning, ReportStrings.StartupVerdict_NotRunning], table.Rows[1].Cells.TakeLast(2).Select(c => c.Text));
        Assert.False(table.Rows[1].Flagged);
        Assert.Equal(CellTone.Unavailable, table.Rows[2].Cells[3].Tone);
        Assert.Equal(CellTone.Unavailable, table.Rows[2].Cells[4].Tone);
        Assert.Equal(ReportStrings.StartupVerdict_Unknown, table.Rows[2].Cells[5].Text);

        var overlays = Table(section, ReportStrings.ColumnOverlayProgram);
        Assert.Equal(["Discord", "Discord.exe", "4242" + ReportStrings.ListSeparator + "4243"], Assert.Single(overlays.Rows).Cells.Select(c => c.Text));
        Assert.Contains(ReportStrings.OverlaysCaveat, Paragraphs(section));
        Assert.Contains(ReportStrings.AdviceNone, Paragraphs(section));
    }

    [Fact]
    public void Startup_section_without_overlays_says_so()
    {
        var section = Build(Report(), ReportSectionIds.Startup);

        Assert.Contains(ReportStrings.EmptyList, Paragraphs(section));
        Assert.Contains(ReportStrings.OverlaysNone, Paragraphs(section));
        Assert.DoesNotContain(section.Blocks.OfType<ReportTable>(), t => t.Headers[0] == ReportStrings.ColumnOverlayProgram);
    }

    [Fact]
    public void Tweaks_section_shows_unknown_as_unavailable()
    {
        var tweaks = FakeConfiguration.DefaultCatalog.RegistryTweaks;
        TweakProposal[] proposals =
        [
            new(tweaks[0], TweakState.NotNeeded, "AutoGameModeEnabled", null, []),
            new(tweaks[1], TweakState.ManualOnly, null, "cyberpunk2077.exe", ["cyberpunk2077.exe", "Intel(R) UHD Graphics 770"]),
            new(tweaks[2], TweakState.Proposed, @"D:\Games\Game\game.exe", "game.exe", ["game.exe", "Composed: Copy with GPU GDI"]),
        ];
        var report = With(s => s with { Gaming = Known(new GamingSettings(Reading<bool>.Unknown(Source, "value out of range"), Known(true))) },
            tweaks: proposals);

        var section = Build(report, ReportSectionIds.Tweaks);

        Assert.Equal(DataStatus.Available, section.Status);
        Assert.Equal(CellTone.Unavailable, Field(section, ReportStrings.GamingGameMode).Tone);
        Assert.Equal(ReportStrings.Yes, Field(section, ReportStrings.GamingBackgroundRecording).Text);
        var table = Table(section, ReportStrings.ColumnTweak);
        Assert.Equal(
            [ReportStrings.Action_RegistryTweak_GameMode, ReportStrings.Action_RegistryTweak_GpuHighPerformance, ReportStrings.Action_RegistryTweak_WindowedFlipModel],
            table.Rows.Select(r => r.Cells[0].Text));
        Assert.Equal([ReportStrings.TweakState_NotNeeded, ReportStrings.TweakState_ManualOnly, ReportStrings.TweakState_Proposed],
            table.Rows.Select(r => r.Cells[1].Text));
        Assert.Equal(
        [
            ReportStrings.TweakNoEvidence,
            string.Format(Russian, ReportStrings.TweakEvidence_GpuHighPerformance, "cyberpunk2077.exe", "Intel(R) UHD Graphics 770"),
            string.Format(Russian, ReportStrings.TweakEvidence_WindowedFlipModel, "game.exe", "Composed: Copy with GPU GDI"),
        ], table.Rows.Select(r => r.Cells[2].Text));
        Assert.Equal([false, true, true], table.Rows.Select(r => r.Flagged));
        Assert.Contains(@"D:\Games\Game\game.exe", table.Rows[2].Cells[0].Tooltip);
        Assert.Contains(ReportStrings.TweaksCaveat, Paragraphs(section));
    }

    [Fact]
    public void Unavailable_startup_and_gaming_are_not_assessed()
    {
        var report = With(s => s with
        {
            Startup = Reading<IReadOnlyList<StartupItem>>.Failure("startup", "registry unreadable"),
            Gaming = Reading<GamingSettings>.Failure("gaming", "registry unreadable"),
        });

        var startup = Build(report, ReportSectionIds.Startup);
        var tweaks = Build(report, ReportSectionIds.Tweaks);

        Assert.Equal(DataStatus.Failed, startup.Status);
        Assert.Contains(ReportStrings.AdviceNotAssessed, Paragraphs(startup));
        Assert.Equal(DataStatus.Failed, tweaks.Status);
        Assert.Equal(CellTone.Unavailable, Field(tweaks, ReportStrings.GamingGameMode).Tone);
        Assert.Equal(CellTone.Unavailable, Field(tweaks, ReportStrings.GamingBackgroundRecording).Tone);
        Assert.Contains(ReportStrings.AdviceNotAssessed, Paragraphs(tweaks));
    }

    [Fact]
    public void Latency_without_series_is_not_assessed()
    {
        var section = Build(Report(), ReportSectionIds.Latency);

        Assert.Equal(EnumText.Format(AdviceModule.Latency), section.Title);
        Assert.Equal(DataStatus.Unknown, section.Status);
        Assert.Empty(section.Blocks.OfType<ReportFieldList>());
        var hint = Assert.Single(section.Blocks.OfType<ReportParagraph>(), p => p.Content.Text == ReportStrings.LatencyNotAssessed).Content;
        Assert.Equal(CellTone.Unavailable, hint.Tone);
        Assert.Contains("не оценивалось", hint.Text);
        Assert.Contains("серии бенчмарка", hint.Text);
        Assert.Contains("трасс", hint.Text);
        Assert.Contains("«Система»", hint.Text);
        Assert.Contains(ReportStrings.AdviceNotAssessed, Paragraphs(section));
    }

    [Fact]
    public void Latency_shows_median_peak_and_advice()
    {
        var series = SampleSeries.Full();
        var advice = LatencyAdvisor.Advise(series.Telemetry, FakeConfiguration.DefaultThresholds.Bottleneck);
        var benchmark = new BenchmarkReport(series, null, [], FakeConfiguration.DefaultThresholds.Comparison);

        var section = Build(Report(benchmark: benchmark, advice: advice), ReportSectionIds.Latency);

        Assert.Equal(DataStatus.Available, section.Status);
        Assert.Equal(series.Id, Field(section, ReportStrings.LatencySeries).Tooltip);
        Assert.Contains(series.Conditions.Target, Field(section, ReportStrings.LatencySeries).Text);
        Assert.Equal(SensorFieldFormatter.FractionPercent(0.3, Russian), Field(section, ReportStrings.BenchmarkDpcTimeMedian).Text);
        Assert.Equal(SensorFieldFormatter.FractionPercent(2.4, Russian), Field(section, ReportStrings.BenchmarkDpcTimePeak).Text);
        Assert.Equal(SensorFieldFormatter.FractionPercent(0.2, Russian), Field(section, ReportStrings.BenchmarkInterruptTimeMedian).Text);
        Assert.Equal(CellTone.Unavailable, Field(section, ReportStrings.BenchmarkInterruptTimePeak).Tone);
        var row = Assert.Single(AdviceTable(section).Rows);
        Assert.Equal(AdviceText.Summary(Assert.Single(advice), Russian), row.Cells[0].Text);
        Assert.Equal(CellTone.Warning, row.Cells[0].Tone);
        Assert.Equal(EnumText.Format(SafetyLevel.L3ManualOnly), row.Cells[3].Text);
        Assert.Contains(ReportStrings.LatencyCaveat, Paragraphs(section));
    }

    [Fact]
    public void Hags_unknown_state_is_unavailable()
    {
        var unknown = Reading<bool>.Unknown("hags", "value absent");

        var section = Build(WithHags(unknown, new HagsExperimentView(null, HagsCheck.StateUnknown, unknown, null, null)), ReportSectionIds.Hags);
        var unread = Build(WithHags(unknown, null), ReportSectionIds.Hags);

        Assert.Equal(ReportStrings.SectionHags, section.Title);
        Assert.Equal(DataStatus.Unknown, section.Status);
        Assert.Equal(CellTone.Unavailable, Field(section, ReportStrings.HagsCurrent).Tone);
        Assert.Equal(EnumText.Format(HagsCheck.StateUnknown), Field(section, ReportStrings.HagsStage).Text);
        Assert.Equal(CellTone.Unavailable, Field(unread, ReportStrings.HagsStage).Tone);
        Assert.Contains(ReportStrings.HagsCaveat, Paragraphs(section));
        Assert.DoesNotContain(ReportStrings.HagsNotStarted, Paragraphs(unread));
    }

    [Fact]
    public void Hags_without_experiment_explains_ab_test()
    {
        var enabled = Reading<bool>.Known(true, SystemSamples.Source);

        var section = Build(WithHags(enabled, new HagsExperimentView(null, HagsCheck.NotStarted, enabled, null, null)), ReportSectionIds.Hags);

        Assert.Equal(DataStatus.Available, section.Status);
        Assert.Equal(ReportStrings.HagsEnabled, Field(section, ReportStrings.HagsCurrent).Text);
        Assert.Equal(EnumText.Format(HagsCheck.NotStarted), Field(section, ReportStrings.HagsStage).Text);
        Assert.Contains(ReportStrings.HagsNotStarted, Paragraphs(section));
        Assert.Contains("«Начать тест»", ReportStrings.HagsNotStarted);
        Assert.Contains("перезагрузите", ReportStrings.HagsNotStarted);
        Assert.Contains("Booster HAGS не переключает", ReportStrings.HagsCaveat);
        Assert.Contains(ReportStrings.HagsCaveat, Paragraphs(section));
        Assert.Empty(section.Blocks.OfType<ReportTable>());
    }

    [Fact]
    public void Hags_completed_shows_verdict_and_manual_step()
    {
        var settings = FakeConfiguration.DefaultThresholds.Comparison;
        var comparison = BenchmarkComparer.Compare(SampleSeries.Completed("base", TakenAt.AddHours(-2)),
            SampleSeries.Completed("candidate", TakenAt.AddHours(-1), fps: 80), settings);
        var experiment = new HagsExperiment(HagsExperiment.CurrentSchemaVersion, TakenAt.AddHours(-2), "game.exe", "base", BaselineEnabled: true,
            HagsExperimentStage.Completed, TakenAt.AddMinutes(-90), "candidate", TakenAt.AddHours(-2));
        var disabled = Reading<bool>.Known(false, SystemSamples.Source);
        var view = new HagsExperimentView(experiment, HagsCheck.Completed, disabled, comparison, HagsVerdicts.From(comparison));
        Assert.Equal(HagsVerdict.Revert, view.Verdict);

        var section = Build(WithHags(disabled, view), ReportSectionIds.Hags);

        Assert.Equal(ReportStrings.HagsDisabled, Field(section, ReportStrings.HagsCurrent).Text);
        Assert.Equal("game.exe", Field(section, ReportStrings.HagsTarget).Text);
        Assert.Equal(ReportStrings.HagsEnabled, Field(section, ReportStrings.HagsBaselineValue).Text);
        Assert.Equal(ReportStrings.HagsDisabled, Field(section, ReportStrings.HagsCandidateValue).Text);
        var verdict = string.Format(Russian, ReportStrings.HagsVerdict_Revert, ReportStrings.HagsDisabled, ReportStrings.HagsEnabled);
        Assert.Equal(CellTone.Warning, Assert.Single(section.Blocks.OfType<ReportParagraph>(), p => p.Content.Text == verdict).Content.Tone);
        Assert.Equal(comparison.Metrics.Select(m => EnumText.Format(m.Metric)),
            Table(section, ReportStrings.ColumnMetric).Rows.Select(r => r.Cells[0].Text));
        Assert.Contains(ReportStrings.HagsManualStep, Paragraphs(section));
        Assert.Contains("ms-settings:display-advancedgraphics", ReportStrings.HagsManualStep);
        Assert.Contains("«Перезагрузка»", ReportStrings.HagsManualStep);
        Assert.Contains(ReportStrings.HagsCaveat, Paragraphs(section));
        Assert.DoesNotContain(ReportStrings.HagsNotStarted, Paragraphs(section));
    }
}
