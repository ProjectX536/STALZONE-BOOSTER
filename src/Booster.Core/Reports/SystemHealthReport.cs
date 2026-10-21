using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Hags;
using Booster.Core.Inventory;
using Booster.Core.Resources;
using Booster.Core.SystemHealth;
using static Booster.Core.Reports.ReportFormat;

namespace Booster.Core.Reports;

public static class SystemHealthReport
{
    public static IReadOnlyList<ReportSection> Build(SystemReport report, ServiceCatalog catalog, StartupThresholds startup, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(culture);
        var composer = new Composer(report, catalog, startup, culture);
        return
        [
            composer.Memory(), composer.Services(), composer.Stability(), composer.Network(), composer.Startup(), composer.Tweaks(),
            composer.Latency(), composer.Hags(),
        ];
    }

    private sealed class Composer(SystemReport report, ServiceCatalog catalog, StartupThresholds startupThresholds, CultureInfo culture)
    {
        private const double BitsPerKilobit = 1e3;
        private const double BitsPerMegabit = 1e6;
        private const double BitsPerGigabit = 1e9;
        private const long BytesPerMegabyte = 1L << 20;

        private SystemDiagnostics System => report.Snapshot.System;

        public ReportSection Memory()
        {
            var writer = new ReportBlockWriter();
            var memory = System.Memory;
            writer.Fields(
            [
                (ReportStrings.SystemPhysicalMemory, Value(memory, m => Format(culture, ReportStrings.SystemPhysicalMemoryFormat,
                    Bytes(m.PhysicalTotalBytes), Bytes(m.PhysicalAvailableBytes), Share(m.PhysicalAvailableBytes, m.PhysicalTotalBytes)))),
                (ReportStrings.SystemCommit, Value(memory, m => Format(culture, ReportStrings.SystemCommitFormat,
                    Bytes(m.CommitTotalBytes), Bytes(m.CommitLimitBytes), Share(m.CommitTotalBytes, m.CommitLimitBytes)))),
                (ReportStrings.SystemCommitPeak, Value(memory, m => Bytes(m.CommitPeakBytes))),
                (ReportStrings.SystemCache, Value(memory, m => Bytes(m.SystemCacheBytes))),
                (ReportStrings.SystemKernelPaged, Value(memory, m => Bytes(m.KernelPagedBytes))),
                (ReportStrings.SystemKernelNonPaged, Value(memory, m => Bytes(m.KernelNonPagedBytes))),
                (ReportStrings.SystemCounts, Value(memory, m => Format(culture, ReportStrings.SystemCountsFormat,
                    Number(m.ProcessCount, culture), Number(m.ThreadCount, culture), Number(m.HandleCount, culture)))),
                (ReportStrings.SystemPagefileMode, Value(MemoryAdvisor.Mode(System.Pagefile), EnumText.Format)),
            ]);
            Pagefiles(writer);
            Processes(writer);
            Advice(writer, AdviceModule.Memory, memory.IsAvailable);
            return Section(ReportSectionIds.SystemMemory, AdviceModule.Memory, memory.Status, writer);
        }

        private void Pagefiles(ReportBlockWriter writer)
        {
            writer.Heading(ReportStrings.PagefileHeading);
            if (!Available(writer, System.Pagefile, out var pagefile))
                return;

            var paths = pagefile.Usage.Select(u => u.Path)
                .Concat(pagefile.Settings.Select(s => s.Path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!NotEmpty(writer, paths))
                return;

            writer.Table([ReportStrings.ColumnPath, ReportStrings.ColumnPagefileConfigured, ReportStrings.ColumnSize, ReportStrings.ColumnUsed,
                    ReportStrings.ColumnPeak, ReportStrings.ColumnFreeOnVolume],
                paths.Select(path =>
                {
                    var usage = pagefile.Usage.FirstOrDefault(u => SamePath(u.Path, path));
                    var setting = pagefile.Settings.FirstOrDefault(s => SamePath(s.Path, path));
                    var noFile = Text(ReportStrings.PagefileNoFile) with { Tone = CellTone.Unavailable };
                    return new ReportRow(
                    [
                        Text(path),
                        Configured(pagefile.AutomaticManaged, setting),
                        usage is null ? noFile : Text(Megabytes(usage.AllocatedMb)),
                        usage is null ? Text(null) : Text(Megabytes(usage.CurrentUsageMb)),
                        usage is null ? Text(null) : Text(Megabytes(usage.PeakUsageMb)),
                        FreeOnVolume(path),
                    ]);
                }));
        }

        private ReportCell Configured(Reading<bool> automatic, PagefileSetting? setting) => setting switch
        {
            { InitialSizeMb: 0, MaximumSizeMb: 0 } => Text(ReportStrings.PagefileSizeBySystem),
            { } manual => Text(Format(culture, ReportStrings.PagefileSizeRangeFormat, Number(manual.InitialSizeMb, culture),
                Number(manual.MaximumSizeMb, culture))),
            _ when automatic is { IsAvailable: true, Value: true } => Text(ReportStrings.PagefileSizeBySystem),
            _ => UnknownCell,
        };

        private ReportCell FreeOnVolume(string path)
        {
            if (MemoryAdvisor.VolumeLetter(path) is not { } letter
                || report.Snapshot.Hardware.Storage is not { IsAvailable: true, Value: { } devices }
                || MemoryAdvisor.FindVolume(devices, letter) is not { } found)
                return UnknownCell;
            return FreeSpace(found.Volume, culture);
        }

        private void Processes(ReportBlockWriter writer)
        {
            writer.Heading(ReportStrings.TopProcessesHeading);
            if (!Available(writer, System.TopMemoryProcesses, out var processes) || !NotEmpty(writer, processes))
                return;

            writer.Table([ReportStrings.ColumnProcess, ReportStrings.ColumnPid, ReportStrings.ColumnPrivateBytes, ReportStrings.ColumnWorkingSet],
                processes.Select(p => new ReportRow(
                    [Text(p.Name), Text(Number(p.ProcessId, culture)), Text(Bytes(p.PrivateBytes)), Text(Bytes(p.WorkingSetBytes))])));
        }

        public ReportSection Services()
        {
            var writer = new ReportBlockWriter();
            var services = System.Services;
            if (Available(writer, services, out var list))
            {
                var assessed = ServiceClassifier.Assess(list, catalog);
                writer.Fields(
                [
                    (ReportStrings.ServicesTotal, Text(Number(list.Count, culture))),
                    .. Enum.GetValues<ServiceCategory>().Select(category =>
                        (EnumText.Format(category), Text(Number(assessed.Count(a => a.Category == category), culture)))),
                ]);

                writer.Heading(ReportStrings.ServicesNotMicrosoftHeading);
                var shown = assessed
                    .Where(a => a.Category is not (ServiceCategory.Microsoft or ServiceCategory.SystemCritical))
                    .OrderByDescending(a => a.ReviewCandidate)
                    .ThenBy(a => a.Category)
                    .ThenBy(a => a.Service.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (NotEmpty(writer, shown))
                {
                    writer.Table([ReportStrings.ColumnServiceName, ReportStrings.ColumnDisplayName, ReportStrings.ColumnPublisher,
                            ReportStrings.ColumnCategory, ReportStrings.ColumnState, ReportStrings.ColumnStartMode, ReportStrings.ColumnDependents],
                        shown.Select(a => new ReportRow(
                        [
                            Text(a.Service.Name) with { Tooltip = a.Service.ImagePath },
                            Text(a.Service.DisplayName),
                            Value(a.Service.Publisher, publisher => publisher),
                            Text(EnumText.Format(a.Category)),
                            Text(EnumText.Format(a.Service.State)),
                            Text(StartMode(a.Service)),
                            Text(Number(a.Service.DependentServices.Count, culture)),
                        ], a.ReviewCandidate)));
                }
            }

            writer.Paragraph(Text(ReportStrings.ServicesCaveat) with { Tone = CellTone.Note });
            Advice(writer, AdviceModule.Services, services.IsAvailable);
            return Section(ReportSectionIds.Services, AdviceModule.Services, services.Status, writer);
        }

        private string StartMode(ServiceInfo service) => service.DelayedAutoStart
            ? Format(culture, ReportStrings.ServiceDelayedStartFormat, EnumText.Format(service.StartMode))
            : EnumText.Format(service.StartMode);

        public ReportSection Stability()
        {
            var writer = new ReportBlockWriter();
            var stability = System.Stability;
            if (Available(writer, stability, out var log))
            {
                writer.Fields([(ReportStrings.StabilityPeriod,
                    Text(Format(culture, ReportStrings.StabilityPeriodFormat, log.Since.ToLocalTime().ToString("d", culture))))]);
                var kinds = StabilityAnalyzer.Summarize(log);
                if (kinds.Count == 0)
                {
                    writer.Paragraph(Text(ReportStrings.StabilityNoEvents));
                }
                else
                {
                    writer.Table([ReportStrings.ColumnEventKind, ReportStrings.ColumnCount, ReportStrings.ColumnLast, ReportStrings.ColumnStabilitySubjects],
                        kinds.Select(kind => new ReportRow(
                        [
                            Text(EnumText.Format(kind.Kind)), Text(Number(kind.Count, culture)), Text(Timestamp(kind.Last, culture)),
                            Text(string.Join(ReportStrings.ListSeparator, kind.Subjects)),
                        ])));
                }

                if (log.Truncated)
                {
                    writer.Paragraph(Text(Format(culture, ReportStrings.StabilityTruncatedFormat, Number(log.Events.Count, culture)))
                        with { Tone = CellTone.Note });
                }
            }

            Advice(writer, AdviceModule.Stability, stability.IsAvailable);
            return Section(ReportSectionIds.Stability, AdviceModule.Stability, stability.Status, writer);
        }

        public ReportSection Network()
        {
            var writer = new ReportBlockWriter();
            var adapters = System.NetworkAdapters;
            writer.Heading(ReportStrings.NetworkAdaptersHeading);
            if (Available(writer, adapters, out var list) && NotEmpty(writer, list))
            {
                writer.Table([ReportStrings.ColumnNetworkAdapter, ReportStrings.ColumnMedium, ReportStrings.ColumnSpeed, ReportStrings.ColumnGateways],
                    list.Select(adapter => new ReportRow(
                    [
                        Text(adapter.Name) with { Tooltip = adapter.Description },
                        Text(EnumText.Format(adapter.Medium)),
                        adapter.LinkSpeedBitsPerSecond is { } speed ? Text(Speed(speed)) : UnknownCell,
                        Text(adapter.Gateways.Count > 0 ? string.Join(ReportStrings.ListSeparator, adapter.Gateways) : ReportStrings.NetworkNoGateway),
                    ])));
            }

            if (report.Network is { } test)
                NetworkTest(writer, test);
            else
                writer.Paragraph(Text(ReportStrings.NetworkNotTested) with { Tone = CellTone.Note });
            writer.Paragraph(Text(ReportStrings.NetworkCaveat) with { Tone = CellTone.Note });
            Advice(writer, AdviceModule.Network, report.Network is not null);
            return Section(ReportSectionIds.Network, AdviceModule.Network, adapters.Status, writer);
        }

        private void NetworkTest(ReportBlockWriter writer, NetworkTestResult test)
        {
            writer.Heading(ReportStrings.NetworkTestHeading);
            writer.Fields(
            [
                (ReportStrings.NetworkTestStartedAt, Text(Timestamp(test.StartedAt, culture))),
                (ReportStrings.NetworkTestDuration, Text(Format(culture, ReportStrings.UnitSeconds, test.Duration.TotalSeconds.ToString("0.#", culture)))),
                (ReportStrings.NetworkTestAdapter, test.Adapter is { } adapter
                    ? Text(adapter.Name)
                    : Text(ReportStrings.NetworkTestNoAdapter) with { Tone = CellTone.Unavailable }),
                (ReportStrings.NetworkUtilization, Value(test.AdapterUtilizationPercent, v => SensorFieldFormatter.Percent(v, culture))),
            ]);
            if (!NotEmpty(writer, test.Targets))
                return;

            writer.Table([ReportStrings.ColumnTarget, ReportStrings.ColumnAddress, ReportStrings.ColumnLatencyMedian, ReportStrings.ColumnJitter,
                    ReportStrings.ColumnLoss, ReportStrings.ColumnReplies],
                test.Targets.Select(target => new ReportRow(
                [
                    Text(EnumText.Format(target.Kind)), Text(target.Address), Value(target.MedianMs, NetworkMs), Value(target.JitterMs, NetworkMs),
                    Value(target.LossPercent, v => SensorFieldFormatter.Percent(v, culture)),
                    Text(Format(culture, ReportStrings.NetworkRepliesFormat, Number(target.Received, culture), Number(target.Sent, culture))),
                ])));
        }

        public ReportSection Startup()
        {
            var writer = new ReportBlockWriter();
            var startup = System.Startup;
            if (Available(writer, startup, out var items) && NotEmpty(writer, items))
            {
                var assessed = StartupAnalyzer.Assess(items, startupThresholds, catalog)
                    .OrderBy(a => a.Verdict)
                    .ThenBy(a => a.Item.Entry.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                writer.Table([ReportStrings.ColumnStartupItem, ReportStrings.ColumnStartupLocation, ReportStrings.ColumnState,
                        ReportStrings.ColumnExecutable, ReportStrings.ColumnPrivateBytes, ReportStrings.ColumnVerdict],
                    assessed.Select(a => new ReportRow(
                    [
                        Text(a.Item.Entry.Name) with { Tooltip = a.Item.Entry.Command },
                        Text(EnumText.Format(a.Item.Entry.Location)),
                        Text(EnumText.Format(a.Item.Entry.State)),
                        a.Item.Entry.ExecutablePath is { } path ? Text(path) : UnknownCell,
                        StartupMemory(a.Item),
                        Text(EnumText.Format(a.Verdict)),
                    ], a.Verdict is StartupVerdict.Candidate or StartupVerdict.NeedsAdmin)));
            }

            writer.Paragraph(Text(ReportStrings.StartupCaveat) with { Tone = CellTone.Note });
            Overlays(writer);
            Advice(writer, AdviceModule.Startup, startup.IsAvailable);
            return Section(ReportSectionIds.Startup, AdviceModule.Startup, startup.Status, writer);
        }

        private ReportCell StartupMemory(StartupItem item) => item switch
        {
            { PrivateBytes: { } bytes } => Text(Bytes(bytes)),
            { Entry.ExecutablePath: null } => UnknownCell,
            _ => Text(ReportStrings.StartupNotRunning) with { Tone = CellTone.Unavailable },
        };

        private void Overlays(ReportBlockWriter writer)
        {
            writer.Heading(ReportStrings.OverlaysHeading);
            if (Available(writer, System.Overlays, out var overlays))
            {
                if (overlays.Count == 0)
                {
                    writer.Paragraph(Text(ReportStrings.OverlaysNone));
                }
                else
                {
                    writer.Table([ReportStrings.ColumnOverlayProgram, ReportStrings.ColumnProcess, ReportStrings.ColumnPid],
                        overlays.Select(o => new ReportRow(
                        [
                            Text(o.Name), Text(o.Process),
                            Text(string.Join(ReportStrings.ListSeparator, o.ProcessIds.Select(id => Number(id, culture)))),
                        ])));
                }
            }

            writer.Paragraph(Text(ReportStrings.OverlaysCaveat) with { Tone = CellTone.Note });
        }

        public ReportSection Tweaks()
        {
            var writer = new ReportBlockWriter();
            var gaming = System.Gaming;
            writer.Fields(
            [
                (ReportStrings.GamingGameMode, GamingValue(gaming, g => g.GameMode)),
                (ReportStrings.GamingBackgroundRecording, GamingValue(gaming, g => g.BackgroundRecording)),
            ]);

            writer.Heading(ReportStrings.TweaksHeading);
            if (NotEmpty(writer, report.Tweaks))
            {
                writer.Table([ReportStrings.ColumnTweak, ReportStrings.ColumnState, ReportStrings.ColumnTweakBasis],
                    report.Tweaks.Select(p => new ReportRow(
                    [
                        Text(TweakText.Name(p.Definition.Id) ?? p.Definition.Id) with
                        {
                            Tooltip = Format(culture, ReportStrings.TweakLocationFormat, p.Definition.SubKey, p.ValueName ?? ReportStrings.TweakValueByGame),
                        },
                        Text(EnumText.Format(p.State)),
                        Text(TweakText.Evidence(p, culture)),
                    ], p.State != TweakState.NotNeeded)));
            }

            writer.Paragraph(Text(ReportStrings.TweaksCaveat) with { Tone = CellTone.Note });
            Advice(writer, AdviceModule.Tweaks, gaming.IsAvailable);
            return Section(ReportSectionIds.Tweaks, AdviceModule.Tweaks, gaming.Status, writer);
        }

        public ReportSection Latency()
        {
            var writer = new ReportBlockWriter();
            var series = report.Benchmark?.Series;
            var telemetry = series?.Telemetry;
            if (series is not null && telemetry is not null)
            {
                writer.Fields(
                [
                    (ReportStrings.LatencySeries, Text(Format(culture, ReportStrings.LatencySeriesFormat, series.Conditions.Target,
                        Timestamp(series.StartedAt, culture))) with { Tooltip = series.Id }),
                    (ReportStrings.BenchmarkDpcTimeMedian, Value(telemetry.DpcTimeMedianPercent, Percent)),
                    (ReportStrings.BenchmarkDpcTimePeak, Value(telemetry.DpcTimePeakPercent, Percent)),
                    (ReportStrings.BenchmarkInterruptTimeMedian, Value(telemetry.InterruptTimeMedianPercent, Percent)),
                    (ReportStrings.BenchmarkInterruptTimePeak, Value(telemetry.InterruptTimePeakPercent, Percent)),
                ]);
            }
            else
            {
                writer.Paragraph(Text(ReportStrings.LatencyNotAssessed) with { Tone = CellTone.Unavailable });
            }

            writer.Paragraph(Text(ReportStrings.LatencyCaveat) with { Tone = CellTone.Note });
            Advice(writer, AdviceModule.Latency,
                telemetry is { DpcTimePeakPercent.IsAvailable: true } or { InterruptTimePeakPercent.IsAvailable: true });
            return Section(ReportSectionIds.Latency, AdviceModule.Latency, telemetry?.DpcTimePeakPercent.Status ?? DataStatus.Unknown, writer);
        }

        public ReportSection Hags()
        {
            var writer = new ReportBlockWriter();
            var current = System.Hags;
            var view = report.Hags;
            var experiment = view?.Experiment;
            List<(string, ReportCell)> fields =
            [
                (ReportStrings.HagsCurrent, Value(current, HagsValue)),
                (ReportStrings.HagsStage, view is null ? UnknownCell : Text(EnumText.Format(view.Check))),
            ];
            if (experiment is not null)
            {
                fields.Add((ReportStrings.HagsTarget, Text(experiment.Target)));
                fields.Add((ReportStrings.HagsBaselineValue, Text(HagsValue(experiment.BaselineEnabled))
                    with { Tooltip = experiment.BaselineSeriesId }));
            }

            if (view is { Check: HagsCheck.Completed, Experiment: { } completed })
            {
                fields.Add((ReportStrings.HagsCandidateValue, Text(HagsValue(!completed.BaselineEnabled))
                    with { Tooltip = completed.CandidateSeriesId }));
            }

            writer.Fields(fields);
            if (view is { Check: HagsCheck.NotStarted })
                writer.Paragraph(Text(ReportStrings.HagsNotStarted));
            if (view is { Verdict: { } verdict, Comparison: { } comparison, Experiment: { } e })
            {
                writer.Heading(ReportStrings.HagsVerdictHeading);
                writer.Paragraph(Text(Format(culture, EnumText.Format(verdict), HagsValue(!e.BaselineEnabled), HagsValue(e.BaselineEnabled)))
                    with { Tone = verdict == HagsVerdict.Revert ? CellTone.Warning : CellTone.Normal });
                ComparisonReport.Metrics(writer, comparison, culture);
                ComparisonReport.Caveats(writer, comparison);
                writer.Paragraph(Text(ReportStrings.HagsManualStep) with { Tone = CellTone.Note });
            }

            writer.Paragraph(Text(ReportStrings.HagsCaveat) with { Tone = CellTone.Note });
            return new ReportSection(ReportSectionIds.Hags, ReportStrings.SectionHags, current.Status, writer.Blocks);
        }

        private static string HagsValue(bool enabled) => enabled ? ReportStrings.HagsEnabled : ReportStrings.HagsDisabled;

        private static ReportCell GamingValue(Reading<GamingSettings> gaming, Func<GamingSettings, Reading<bool>> select) =>
            gaming is { IsAvailable: true, Value: { } settings } ? Value(select(settings), Bool) : Value(gaming, _ => string.Empty);

        private void Advice(ReportBlockWriter writer, AdviceModule module, bool assessed)
        {
            writer.Heading(ReportStrings.AdviceHeading);
            var advice = report.Advice.Where(a => a.Module == module).ToList();
            if (advice.Count == 0)
            {
                writer.Paragraph(assessed
                    ? Text(ReportStrings.AdviceNone)
                    : Text(ReportStrings.AdviceNotAssessed) with { Tone = CellTone.Unavailable });
                return;
            }

            writer.Table([ReportStrings.ColumnRecommendation, ReportStrings.ColumnAdviceAction, ReportStrings.ColumnAdviceRisk, ReportStrings.ColumnLevel],
                advice.Select(a => new ReportRow(
                [
                    Text(AdviceText.Summary(a, culture)) with { Tone = a.Severity == FindingSeverity.Warning ? CellTone.Warning : CellTone.Info },
                    Text(AdviceText.Action(a)),
                    Text(AdviceText.Risk(a)),
                    Text(EnumText.Format(a.Level)),
                ])));
        }

        private static ReportSection Section(string id, AdviceModule module, DataStatus status, ReportBlockWriter writer) =>
            new(id, EnumText.Format(module), status, writer.Blocks);

        private static bool Available<T>(ReportBlockWriter writer, Reading<T> reading, [NotNullWhen(true)] out T? value)
            where T : class
        {
            if (reading is { IsAvailable: true, Value: { } known })
            {
                value = known;
                return true;
            }

            writer.Paragraph(Value(reading, _ => string.Empty));
            value = null;
            return false;
        }

        private static bool NotEmpty<T>(ReportBlockWriter writer, IReadOnlyCollection<T> items)
        {
            if (items.Count > 0)
                return true;
            writer.Paragraph(Text(ReportStrings.EmptyList) with { Tone = CellTone.Unavailable });
            return false;
        }

        private static bool SamePath(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        private string Bytes(long value) => ByteSizeFormatter.Format(value, culture);

        private string Megabytes(int megabytes) => Bytes(megabytes * BytesPerMegabyte);

        private string Share(long part, long whole) =>
            whole > 0 ? ((double)part / whole).ToString("P0", culture) : ReadingFormatter.StatusText(DataStatus.Unknown);

        private string Percent(double value) => SensorFieldFormatter.FractionPercent(value, culture);

        private string NetworkMs(double value) => Format(culture, ReportStrings.UnitMs, value.ToString("0.#", culture));

        private string Speed(long bitsPerSecond) => bitsPerSecond switch
        {
            >= (long)BitsPerGigabit => Format(culture, ReportStrings.UnitGbps, (bitsPerSecond / BitsPerGigabit).ToString("0.#", culture)),
            >= (long)BitsPerMegabit => Format(culture, ReportStrings.UnitMbps, (bitsPerSecond / BitsPerMegabit).ToString("0.#", culture)),
            _ => Format(culture, ReportStrings.UnitKbps, (bitsPerSecond / BitsPerKilobit).ToString("0.#", culture)),
        };
    }
}
