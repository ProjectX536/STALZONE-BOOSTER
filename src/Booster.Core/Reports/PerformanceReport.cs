using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.Metrics;
using Booster.Core.Resources;
using Booster.Core.Sensors;
using static Booster.Core.Reports.ReportFormat;

namespace Booster.Core.Reports;

/// <summary>
/// Разделы бенчмарка: серия (условия, прогоны, итог, ограничения, рекомендации, сигналы) и сравнение с базой. Booster ничего
/// не меняет сам: рекомендации — только советы, выводы сравнения — только по повторным прогонам и с оговорками.
/// </summary>
public static class PerformanceReport
{
    /// <summary>Раздел серии; null-серия — раздел с текстом «серий ещё не было».</summary>
    public static ReportSection BuildSeries(BenchmarkReport? report, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var writer = new ReportBlockWriter();
        if (report is null)
            writer.Paragraph(Text(ReportStrings.BenchmarkNoSeries));
        else
            new SeriesComposer(report.Series, report.Settings, writer, culture).Compose();
        return new ReportSection(ReportSectionIds.Benchmark, ReportStrings.SectionBenchmark, null, writer.Blocks);
    }

    /// <summary>Раздел сравнения; без сравнения — null (раздела нет).</summary>
    public static ReportSection? BuildComparison(BenchmarkReport? report, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return report?.Comparison is { } comparison ? ComparisonReport.Build(comparison, report.Chain, report.Settings, culture) : null;
    }

    private sealed class SeriesComposer(BenchmarkSeries series, ComparisonSettings settings, ReportBlockWriter writer, CultureInfo culture)
    {
        public void Compose()
        {
            Overview();
            // Захват не состоялся: кроме статуса и причины, показывать нечего.
            if (series.Status != FrameCaptureStatus.Completed && series.Runs.Count == 0)
                return;

            Conditions();
            Runs();
            Summary();
            Presentation();
            Bottleneck();
            Recommendations();
            Telemetry();
        }

        private void Overview()
        {
            var completed = series.Status == FrameCaptureStatus.Completed;
            var fields = new List<(string, ReportCell)>
            {
                (ReportStrings.BenchmarkStatus, Text(EnumText.Format(series.Status)) with { Tone = completed ? CellTone.Normal : CellTone.Warning }),
                (ReportStrings.BenchmarkTarget, Text(series.Conditions.Target)),
                (ReportStrings.BenchmarkStartedAt, Text(Timestamp(series.StartedAt, culture))),
                (ReportStrings.BenchmarkSchedule, Text(Format(culture, ReportStrings.BenchmarkScheduleFormat, Number(series.Schedule.RunCount, culture),
                    Seconds(series.Schedule.RunDuration), Seconds(series.Schedule.Pause), Seconds(series.Schedule.StartDelay)))),
            };
            if (series.Detail is { } detail)
                fields.Add((ReportStrings.BenchmarkDetail, Text(detail) with { Tone = CellTone.Note }));
            if (series.CsvPath is { } csv)
                fields.Add((ReportStrings.BenchmarkCsv, Text(csv)));
            writer.Fields(fields);
        }

        private void Conditions()
        {
            var conditions = series.Conditions;
            var display = conditions.Display;
            writer.Heading(ReportStrings.BenchmarkConditionsHeading);
            writer.Fields(
            [
                (ReportStrings.BenchmarkConditionRenderAdapter, Value(conditions.RenderAdapter, name => name)),
                (ReportStrings.BenchmarkConditionDriver, Value(conditions.DriverVersion, version => version)),
                (ReportStrings.BenchmarkMonitor, Display(display, mode => mode.MonitorName is { } name ? Text(name) : UnknownCell)),
                (ReportStrings.BenchmarkConditionResolution, Display(display, mode => Text(Format(culture, ReportStrings.ResolutionFormat,
                    Number(mode.Width, culture), Number(mode.Height, culture))))),
                (ReportStrings.BenchmarkConditionRefreshRate, Display(display, mode => Text(Hz(mode.RefreshRateHz)))),
                (ReportStrings.BenchmarkConditionHdr, Display(display, mode => Flag(mode.HdrEnabled))),
                (ReportStrings.BenchmarkConditionVrr, Display(display, mode => Flag(mode.VrrEnabled))),
                (ReportStrings.BenchmarkConditionPowerPlan, Value(conditions.PowerPlan, plan => plan)),
                (ReportStrings.BenchmarkConditionPowerSource, Text(EnumText.Format(conditions.PowerSource))
                    with { Tone = conditions.PowerSource == PowerSource.Unknown ? CellTone.Unavailable : CellTone.Normal }),
                (ReportStrings.BenchmarkConditionProfile, Text(conditions.ActiveProfile ?? ReportStrings.BenchmarkNoProfile)),
                (ReportStrings.BenchmarkConditionGraphicsProfile, Noted(conditions.Notes.GraphicsProfile)),
                (ReportStrings.BenchmarkConditionScene, Noted(conditions.Notes.Scene)),
                (ReportStrings.BenchmarkConditionWindowMode, Noted(conditions.Notes.WindowMode)),
                (ReportStrings.BenchmarkChange, Noted(conditions.Notes.Change)),
                (ReportStrings.BenchmarkBackground, Background(conditions.Background)),
                (ReportStrings.BenchmarkNetwork, Text(ReportStrings.BenchmarkNetworkNotMeasured) with { Tone = CellTone.Unavailable }),
            ]);
        }

        private void Runs()
        {
            writer.Heading(ReportStrings.BenchmarkRunsHeading);
            if (series.Runs.Count == 0)
            {
                writer.Paragraph(Text(ReportStrings.BenchmarkNoRuns) with { Tone = CellTone.Unavailable });
                return;
            }

            writer.Table(
                [
                    ReportStrings.ColumnRun, ReportStrings.ColumnFrames, ReportStrings.MetricsAverageFps, ReportStrings.MetricsOnePercentLow,
                    ReportStrings.MetricsP50, ReportStrings.MetricsP95, ReportStrings.MetricsP99, ReportStrings.ColumnStuttersPerMinute,
                    ReportStrings.ColumnStatus,
                ],
                series.Runs.Select(Run));
        }

        /// <summary>Прогон без метрик (мало кадров) виден со статусом, но без чисел: прочерк, а не ноль.</summary>
        private ReportRow Run(BenchmarkRun run)
        {
            var metrics = run.Metrics is { Status: FrameMetricsStatus.Ok, Metrics: { } known } ? known : null;
            ReportCell Metric(Func<FrameMetrics, string> format) => metrics is null ? NotMeasuredCell : Text(format(metrics));

            return new ReportRow(
            [
                Text(Number(run.Number, culture)),
                Text(Number(run.Frames.Frames, culture)),
                Metric(m => m.AverageFps.ToString("0.0", culture)),
                Metric(m => m.OnePercentLowFps.ToString("0.0", culture)),
                Metric(m => Milliseconds(m.FrameTimeP50Ms, culture)),
                Metric(m => Milliseconds(m.FrameTimeP95Ms, culture)),
                Metric(m => Milliseconds(m.FrameTimeP99Ms, culture)),
                metrics is not null && run.Stutter is { } stutter ? Text(stutter.StuttersPerMinute.ToString("0.0", culture)) : NotMeasuredCell,
                Text(EnumText.Format(run.Metrics.Status)) with { Tone = metrics is null ? CellTone.Warning : CellTone.Normal },
            ], Flagged: metrics is null);
        }

        /// <summary>Медиана и разброс по годным прогонам; меньше <c>minimumRuns</c> — оговорка.</summary>
        private void Summary()
        {
            writer.Heading(ReportStrings.BenchmarkSummaryHeading);
            if (series.Summary is not { } summary)
            {
                writer.Paragraph(Text(ReportStrings.BenchmarkNoValidRuns) with { Tone = CellTone.Unavailable });
                return;
            }

            var variation = summary.AverageFpsVariationPercent;
            writer.Fields(
            [
                (ReportStrings.BenchmarkValidRuns, Text(Format(culture, ReportStrings.BenchmarkValidRunsFormat, Number(summary.ValidRuns, culture),
                    Number(series.Runs.Count, culture)))),
                (ReportStrings.BenchmarkVariation, Text(Format(culture, ReportStrings.UnitPercent, variation.ToString("0.0", culture)))
                    with { Tone = variation > settings.RunVariationPercent ? CellTone.Warning : CellTone.Normal }),
            ]);
            writer.Table([ReportStrings.ColumnMetric, ReportStrings.ColumnMedian, ReportStrings.ColumnMinMax],
                SummaryMetrics(summary).Select(m => new ReportRow(
                [
                    Text(EnumText.Format(m.Metric)), Text(MetricNumber(m.Metric, m.Spread.Median, culture)), Text(Range(m.Metric, m.Spread, culture)),
                ])));
            if (summary.ValidRuns < settings.MinimumRuns)
            {
                writer.Paragraph(Text(Format(culture, ReportStrings.BenchmarkTooFewRunsFormat, Number(summary.ValidRuns, culture),
                    Number(settings.MinimumRuns, culture))) with { Tone = CellTone.Note });
            }
        }

        /// <summary>Режим вывода и доли кадров — медианы по годным прогонам; данных нет — «Неизвестно».</summary>
        private void Presentation()
        {
            var frames = series.Runs.Where(run => run.Metrics.Status == FrameMetricsStatus.Ok).Select(run => run.Frames).ToList();
            var modes = series.Conditions.PresentModes;
            writer.Heading(ReportStrings.BenchmarkPresentationHeading);
            writer.Fields(
            [
                (ReportStrings.BenchmarkConditionPresentModes, modes.Count == 0 ? UnknownCell : Text(string.Join(ReportStrings.ListSeparator, modes))),
                (ReportStrings.BenchmarkSyncedShare, Share(MedianOf(frames, f => f.SyncedShare))),
                (ReportStrings.BenchmarkGpuBusyShare, Share(MedianOf(frames, f => f.GpuBusyShare))),
                (ReportStrings.BenchmarkCpuBusyShare, Share(MedianOf(frames, f => f.CpuBusyShare))),
                (ReportStrings.BenchmarkSyncInterval, MedianOf(frames, f => f.TypicalSyncInterval) is { } interval
                    ? Text(interval.ToString("0", culture))
                    : UnknownCell),
            ]);
        }

        private void Bottleneck()
        {
            writer.Heading(ReportStrings.BenchmarkBottleneckHeading);
            if (series.Bottleneck is not { } assessment)
            {
                writer.Paragraph(Text(ReportStrings.BenchmarkNoAnalysis) with { Tone = CellTone.Unavailable });
                return;
            }

            writer.Fields([(ReportStrings.BenchmarkGpuLoad, Text(EnumText.Format(assessment.GpuLoad))
                with { Tone = assessment.GpuLoad == GpuLoadState.Unknown ? CellTone.Unavailable : CellTone.Normal })]);
            foreach (var finding in assessment.Findings)
            {
                writer.Paragraph(Text(Format(culture, ReportStrings.BenchmarkFindingFormat, EnumText.Format(finding.Class),
                    EnumText.Format(finding.Confidence))) with
                {
                    Tone = finding.Confidence >= AnalysisConfidence.Medium ? CellTone.Warning : CellTone.Normal,
                });
                writer.Table([ReportStrings.ColumnEvidence, ReportStrings.ColumnValue, ReportStrings.ColumnThreshold, ReportStrings.ColumnSupports],
                    finding.Evidence.Select(EvidenceRow));
            }

            if (assessment.Findings.Count == 0)
                writer.Paragraph(Text(ReportStrings.BenchmarkNoBottleneck));
            if (assessment.MissingData.Count > 0)
            {
                writer.Fields([(ReportStrings.BenchmarkMissingData, Text(string.Join(ReportStrings.ListSeparator,
                    assessment.MissingData.Select(kind => EnumText.Format(kind)))) with { Tone = CellTone.Unavailable })]);
            }
        }

        /// <summary>Сигнал, значение, порог и «за/против»: видно, почему уверенность такая, а не выше.</summary>
        private ReportRow EvidenceRow(Evidence evidence) => new(
        [
            EvidenceName(evidence),
            Value(evidence.Value, value => EvidenceValue(evidence, value)),
            evidence.Threshold is { } threshold ? Text(EvidenceValue(evidence, threshold)) : NotMeasuredCell,
            Text(evidence.Supports ? ReportStrings.EvidenceFor : ReportStrings.EvidenceAgainst),
        ]);

        private ReportCell EvidenceName(Evidence evidence)
        {
            var name = EnumText.Format(evidence.Kind);
            if (ThrottleSignalOf(evidence) is { } signal)
                return Text(Format(culture, ReportStrings.EvidenceDetailFormat, name, EnumText.Format(signal)));
            return evidence.Detail switch
            {
                BottleneckAnalyzer.PeakDetail => Text(Format(culture, ReportStrings.EvidenceDetailFormat, name, ReportStrings.EvidencePeak)),
                BottleneckAnalyzer.LimiterDetail => Text(name) with { Tooltip = ReportStrings.EvidenceLimiterNote },
                _ => Text(name),
            };
        }

        /// <summary>Значение в единицах сигнала: доли кадров 0..1 — в процентах, пик троттлинга — в единицах его признака.</summary>
        private string EvidenceValue(Evidence evidence, double value) => evidence.Kind switch
        {
            EvidenceKind.GpuBusyFrameShare or EvidenceKind.CpuBusyFrameShare or EvidenceKind.SyncedFrames => Percent(value * 100),
            EvidenceKind.GpuUtilization or EvidenceKind.BusiestThread or EvidenceKind.SystemCpu or EvidenceKind.VramUsage
                or EvidenceKind.AvailableMemory or EvidenceKind.CommitCharge or EvidenceKind.DiskBusy => Percent(value),
            EvidenceKind.FpsAtRefreshRate => value.ToString("0.0", culture),
            EvidenceKind.UniformFrameTimes or EvidenceKind.PercentileRatio => value.ToString("0.00", culture),
            EvidenceKind.PageReads => PerSecond(value),
            EvidenceKind.DiskLatency => Milliseconds(value, culture),
            EvidenceKind.DpcTime or EvidenceKind.InterruptTime => FractionPercent(value),
            EvidenceKind.StutterRate => Format(culture, ReportStrings.UnitPerMinute, value.ToString("0.0", culture)),
            EvidenceKind.Throttle when ThrottleSignalOf(evidence) is { } signal => Peak(signal, value, culture),
            _ => value.ToString("0.##", culture),
        };

        private static ThrottleSignal? ThrottleSignalOf(Evidence evidence) =>
            evidence.Kind == EvidenceKind.Throttle && Enum.TryParse<ThrottleSignal>(evidence.Detail, out var signal) ? signal : null;

        private void Recommendations()
        {
            writer.Heading(ReportStrings.BenchmarkRecommendationsHeading);
            if (series.Recommendations.Count == 0)
            {
                writer.Paragraph(Text(ReportStrings.BenchmarkNoRecommendations));
                return;
            }

            writer.Table([ReportStrings.ColumnRecommendation, ReportStrings.ColumnBasis], series.Recommendations.Select(recommendation => new ReportRow(
            [
                Text(Format(culture, EnumText.Format(recommendation.Id), string.Join(ReportStrings.ListSeparator, recommendation.Arguments))),
                Basis(recommendation),
            ])));
            writer.Paragraph(Text(ReportStrings.BenchmarkRecommendationsNote) with { Tone = CellTone.Note });
        }

        private ReportCell Basis(Recommendation recommendation) => (recommendation.Because, recommendation.Confidence) switch
        {
            ({ } because, { } confidence) => Text(Format(culture, ReportStrings.BenchmarkBasisFormat,
                EnumText.Format(because), EnumText.Format(confidence))),
            ({ } because, null) => Text(EnumText.Format(because)),
            _ => NotMeasuredCell,
        };

        /// <summary>Сводка датчиков за серию и признаки троттлинга; без замеров датчиков — так и сказано.</summary>
        private void Telemetry()
        {
            var telemetry = series.Telemetry;
            writer.Heading(ReportStrings.BenchmarkTelemetryHeading);
            writer.Fields(
            [
                (ReportStrings.BenchmarkSampleCount, Text(Number(telemetry.SampleCount, culture))),
                (ReportStrings.BenchmarkGpuUtilization, Value(telemetry.GpuUtilizationPercent, Percent)),
                (ReportStrings.BenchmarkVramMedian, Value(telemetry.VramUsedMedianPercent, Percent)),
                (ReportStrings.BenchmarkVramPeak, Value(telemetry.VramUsedPeakPercent, Percent)),
                (ReportStrings.BenchmarkGpuTemperaturePeak, Value(telemetry.GpuTemperaturePeakC, value => SensorFieldFormatter.Celsius(value, culture))),
                (ReportStrings.BenchmarkBusiestThread, Value(telemetry.BusiestThreadPercent, Percent)),
                (ReportStrings.BenchmarkTargetCpu, Value(telemetry.TargetCpuPercent, Percent)),
                (ReportStrings.BenchmarkSystemCpu, Value(telemetry.SystemCpuPercent, Percent)),
                (ReportStrings.BenchmarkAvailableMemoryMin, Value(telemetry.AvailableMemoryMinPercent, Percent)),
                (ReportStrings.BenchmarkCommitPeak, Value(telemetry.CommitPeakPercent, Percent)),
                (ReportStrings.BenchmarkPageReads, Value(telemetry.PageReadsPerSecond, PerSecond)),
                (ReportStrings.BenchmarkDiskBusy, Value(telemetry.DiskBusyPercent, Percent)),
                (ReportStrings.BenchmarkDiskLatency, Value(telemetry.DiskLatencyMs, value => Milliseconds(value, culture))),
                (ReportStrings.BenchmarkDpcTimeMedian, Value(telemetry.DpcTimeMedianPercent, FractionPercent)),
                (ReportStrings.BenchmarkDpcTimePeak, Value(telemetry.DpcTimePeakPercent, FractionPercent)),
                (ReportStrings.BenchmarkInterruptTimeMedian, Value(telemetry.InterruptTimeMedianPercent, FractionPercent)),
                (ReportStrings.BenchmarkInterruptTimePeak, Value(telemetry.InterruptTimePeakPercent, FractionPercent)),
            ]);

            if (telemetry.Throttle is { } throttle)
            {
                writer.Add(Throttling(throttle, culture));
                return;
            }

            writer.Heading(ReportStrings.ThrottleHeading);
            writer.Paragraph(Text(ReportStrings.BenchmarkNoSensorSamples) with { Tone = CellTone.Unavailable });
        }

        /// <summary>Поле режима дисплея с подсказкой-источником; режима нет — его статус.</summary>
        private static ReportCell Display(Reading<DisplayMode> display, Func<DisplayMode, ReportCell> cell)
        {
            if (display is not { IsAvailable: true, Value: { } mode })
                return Value(display, _ => string.Empty);
            var value = cell(mode);
            return value with { Tooltip = value.Tooltip ?? ReadingFormatter.SourceText(display.Source, display.Note) };
        }

        private static ReportCell Flag(bool? value) => value is { } known ? Text(Bool(known)) : UnknownCell;

        /// <summary>Пометка пользователя; не указана — так и сказано, а не пусто.</summary>
        private static ReportCell Noted(string? note) => string.IsNullOrWhiteSpace(note)
            ? Text(ReportStrings.BenchmarkNotNoted) with { Tone = CellTone.Unavailable }
            : Text(note.Trim());

        private ReportCell Background(IReadOnlyList<BackgroundProcess> processes) => processes.Count == 0
            ? Text(ReportStrings.BenchmarkNoBackground)
            : Text(string.Join(ReportStrings.ListSeparator, processes.Select(process =>
                Format(culture, ReportStrings.BenchmarkBackgroundFormat, process.Name, Percent(process.CpuPercent)))));

        private ReportCell Share(double? share) => share is { } known ? Text(Percent(known * 100)) : UnknownCell;

        private static double? MedianOf(IEnumerable<FrameStats> frames, Func<FrameStats, double?> field)
        {
            var values = frames.Select(field).OfType<double>().ToArray();
            return values.Length == 0 ? null : Median.Of(values);
        }

        private string Seconds(TimeSpan value) => value.TotalSeconds.ToString("0.#", culture);

        private string Hz(double value) => Format(culture, ReportStrings.UnitHz, value.ToString("0.###", culture));

        private string Percent(double value) => SensorFieldFormatter.Percent(value, culture);

        private string FractionPercent(double value) => SensorFieldFormatter.FractionPercent(value, culture);

        private string PerSecond(double value) => Format(culture, ReportStrings.UnitPerSecond, value.ToString("0", culture));
    }
}
