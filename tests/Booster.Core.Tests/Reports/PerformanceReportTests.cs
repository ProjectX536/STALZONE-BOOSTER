using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.Metrics;
using Booster.Core.Reports;
using Booster.Core.Resources;
using Booster.Core.Sensors;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.Reports;

public class PerformanceReportTests
{
    private static readonly CultureInfo Russian = new("ru-RU");
    private static readonly ComparisonSettings Settings = FakeConfiguration.DefaultThresholds.Comparison;

    private static BenchmarkReport Report(BenchmarkSeries series, BenchmarkComparison? comparison = null,
        IReadOnlyList<BenchmarkComparison>? chain = null) => new(series, comparison, chain ?? [], Settings);

    private static ReportSection Series(BenchmarkSeries series) => PerformanceReport.BuildSeries(Report(series), Russian);

    private static ReportCell Field(ReportSection section, string label) =>
        Assert.Single(section.Blocks.OfType<ReportFieldList>().SelectMany(l => l.Fields), f => f.Label == label).Value;

    private static ReportTable TableWith(ReportSection section, string header) =>
        Assert.Single(section.Blocks.OfType<ReportTable>(), t => t.Headers.Contains(header));

    private static IEnumerable<ReportCell> Paragraphs(ReportSection section) => section.Blocks.OfType<ReportParagraph>().Select(p => p.Content);

    /// <summary>Серия той же игры с тремя годными прогонами: прочие метрики одинаковые, сравнение решают средний FPS и 1% low.</summary>
    private static BenchmarkSeries Valid(string id, int minutes, MetricSpread averageFps, MetricSpread onePercentLow, string? change = "Новый драйвер")
    {
        var series = SampleSeries.Full(id, minutes);
        var same = new MetricSpread(10, 9.5, 10.5);
        return series with
        {
            Summary = new BenchmarkSummary(3, averageFps, onePercentLow, same, same, new MetricSpread(1, 1, 1), 2),
            Conditions = series.Conditions with { Notes = series.Conditions.Notes with { Change = change } },
        };
    }

    [Fact]
    public void Series_section_lists_conditions_runs_findings_and_recommendations()
    {
        var section = Series(SampleSeries.Full());

        Assert.Equal(ReportSectionIds.Benchmark, section.Id);
        Assert.Equal(ReportStrings.SectionBenchmark, section.Title);
        Assert.Equal(EnumText.Format(FrameCaptureStatus.Completed), Field(section, ReportStrings.BenchmarkStatus).Text);
        Assert.Equal("game.exe", Field(section, ReportStrings.BenchmarkTarget).Text);
        Assert.Equal("3 × 30 с, пауза 10 с, задержка старта 10 с", Field(section, ReportStrings.BenchmarkSchedule).Text);
        Assert.Equal(@"C:\Data\captures\a.csv", Field(section, ReportStrings.BenchmarkCsv).Text);

        Assert.Contains(section.Blocks.OfType<ReportHeading>(), h => h.Text == ReportStrings.BenchmarkConditionsHeading);
        Assert.Equal("GeForce RTX 4070", Field(section, ReportStrings.BenchmarkConditionRenderAdapter).Text);
        var driver = Field(section, ReportStrings.BenchmarkConditionDriver);
        Assert.Equal(CellTone.Unavailable, driver.Tone);
        Assert.Contains("driver not reported", driver.Tooltip, StringComparison.Ordinal);
        Assert.Equal("Monitor", Field(section, ReportStrings.BenchmarkMonitor).Text);
        Assert.Equal("2560 × 1440", Field(section, ReportStrings.BenchmarkConditionResolution).Text);
        Assert.Equal("143,856 Гц", Field(section, ReportStrings.BenchmarkConditionRefreshRate).Text);
        Assert.Equal(ReportStrings.No, Field(section, ReportStrings.BenchmarkConditionHdr).Text);
        Assert.Equal(CellTone.Unavailable, Field(section, ReportStrings.BenchmarkConditionVrr).Tone);
        var plan = Field(section, ReportStrings.BenchmarkConditionPowerPlan);
        Assert.Equal(EnumText.Format(DataStatus.Failed), plan.Text);
        Assert.Contains("read failed", plan.Tooltip, StringComparison.Ordinal);
        Assert.Equal(EnumText.Format(PowerSource.Battery), Field(section, ReportStrings.BenchmarkConditionPowerSource).Text);
        Assert.Equal("Соревновательный", Field(section, ReportStrings.BenchmarkConditionProfile).Text);
        Assert.Equal("Высокие", Field(section, ReportStrings.BenchmarkConditionGraphicsProfile).Text);
        Assert.Equal("Город", Field(section, ReportStrings.BenchmarkConditionScene).Text);
        Assert.Equal(ReportStrings.BenchmarkNotNoted, Field(section, ReportStrings.BenchmarkConditionWindowMode).Text);
        Assert.Equal("Новый драйвер", Field(section, ReportStrings.BenchmarkChange).Text);
        Assert.Equal("browser.exe (8 %)", Field(section, ReportStrings.BenchmarkBackground).Text);
        Assert.Equal(ReportStrings.BenchmarkNetworkNotMeasured, Field(section, ReportStrings.BenchmarkNetwork).Text);

        var runs = TableWith(section, ReportStrings.ColumnRun);
        Assert.Equal(2, runs.Rows.Count);
        Assert.Equal(["1", "3600", "120,0", "80,5", "8,30 мс", "11,10 мс", "14,20 мс", "4,0", EnumText.Format(FrameMetricsStatus.Ok)],
            runs.Rows[0].Cells.Select(c => c.Text));
        Assert.False(runs.Rows[0].Flagged);

        var summary = TableWith(section, ReportStrings.ColumnMedian);
        Assert.Equal([EnumText.Format(ComparedMetric.AverageFps), "120,0", "118,0–122,0"], summary.Rows[0].Cells.Select(c => c.Text));
        Assert.Equal("1 из 2", Field(section, ReportStrings.BenchmarkValidRuns).Text);

        Assert.Equal("Hardware: Independent Flip", Field(section, ReportStrings.BenchmarkConditionPresentModes).Text);
        Assert.Equal("10 %", Field(section, ReportStrings.BenchmarkSyncedShare).Text);
        Assert.Equal("95 %", Field(section, ReportStrings.BenchmarkGpuBusyShare).Text);
        Assert.Equal(CellTone.Unavailable, Field(section, ReportStrings.BenchmarkCpuBusyShare).Tone);

        Assert.Equal(EnumText.Format(GpuLoadState.Saturated), Field(section, ReportStrings.BenchmarkGpuLoad).Text);
        var finding = Assert.Single(Paragraphs(section),
            p => p.Text == $"{EnumText.Format(BottleneckClass.GpuLimited)} — уверенность: {EnumText.Format(AnalysisConfidence.High)}");
        Assert.Equal(CellTone.Warning, finding.Tone);
        var evidence = TableWith(section, ReportStrings.ColumnSupports);
        Assert.Equal([EnumText.Format(EvidenceKind.GpuBusyFrameShare), "95 %", "90 %", ReportStrings.EvidenceFor],
            Assert.Single(evidence.Rows).Cells.Select(c => c.Text));
        Assert.Equal(EnumText.Format(EvidenceKind.DiskLatency), Field(section, ReportStrings.BenchmarkMissingData).Text);

        var recommendations = TableWith(section, ReportStrings.ColumnRecommendation);
        Assert.Equal(2, recommendations.Rows.Count);
        Assert.Contains("browser.exe", recommendations.Rows[0].Cells[0].Text, StringComparison.Ordinal);
        Assert.Equal(ReportStrings.NotMeasured, recommendations.Rows[0].Cells[1].Text);
        Assert.Equal(EnumText.Format(RecommendationId.ReduceGpuLoad), recommendations.Rows[1].Cells[0].Text);
        Assert.Equal($"{EnumText.Format(BottleneckClass.GpuLimited)}, уверенность: {EnumText.Format(AnalysisConfidence.High)}",
            recommendations.Rows[1].Cells[1].Text);
        Assert.Contains(Paragraphs(section), p => p.Text == ReportStrings.BenchmarkRecommendationsNote);

        Assert.Equal("30", Field(section, ReportStrings.BenchmarkSampleCount).Text);
        Assert.Equal("64 %", Field(section, ReportStrings.BenchmarkBusiestThread).Text);
        Assert.Equal(CellTone.Unavailable, Field(section, ReportStrings.BenchmarkVramMedian).Tone);
        Assert.Contains(section.Blocks.OfType<ReportHeading>(), h => h.Text == ReportStrings.ThrottleHeading);
        Assert.Equal(EnumText.Format(ThrottleSignal.GpuDriverPowerLimit),
            Assert.Single(TableWith(section, ReportStrings.ColumnSignal).Rows).Cells[0].Text);
    }

    [Fact]
    public void Latency_telemetry_lines()
    {
        var series = SampleSeries.Full();
        var section = Series(series with
        {
            Bottleneck = new BottleneckAssessment(
                [new BottleneckFinding(BottleneckClass.DriverLatency, AnalysisConfidence.Low,
                    [new Evidence(EvidenceKind.DpcTime, Reading<double>.Known(2.4, "test"), 2, true, BottleneckAnalyzer.PeakDetail)])],
                GpuLoadState.Normal, []),
        });

        Assert.Equal("0,3 %", Field(section, ReportStrings.BenchmarkDpcTimeMedian).Text);
        Assert.Equal("2,4 %", Field(section, ReportStrings.BenchmarkDpcTimePeak).Text);
        Assert.Equal("0,2 %", Field(section, ReportStrings.BenchmarkInterruptTimeMedian).Text);
        var interruptPeak = Field(section, ReportStrings.BenchmarkInterruptTimePeak);
        Assert.Equal(EnumText.Format(DataStatus.Unknown), interruptPeak.Text);
        Assert.Equal(CellTone.Unavailable, interruptPeak.Tone);
        Assert.Contains("first reading", interruptPeak.Tooltip, StringComparison.Ordinal);

        Assert.Equal(
            [$"{EnumText.Format(EvidenceKind.DpcTime)}: {ReportStrings.EvidencePeak}", "2,4 %", "2,0 %", ReportStrings.EvidenceFor],
            Assert.Single(TableWith(section, ReportStrings.ColumnSupports).Rows).Cells.Select(c => c.Text));
    }

    [Fact]
    public void Insufficient_run_shown_with_status_and_no_metrics()
    {
        var row = TableWith(Series(SampleSeries.Full()), ReportStrings.ColumnRun).Rows[1];

        Assert.True(row.Flagged);
        Assert.Equal("2", row.Cells[0].Text);
        Assert.Equal("11", row.Cells[1].Text);
        Assert.All(row.Cells.Skip(2).Take(6), cell =>
        {
            Assert.Equal(ReportStrings.NotMeasured, cell.Text);
            Assert.Equal(CellTone.Unavailable, cell.Tone);
        });
        Assert.Equal(EnumText.Format(FrameMetricsStatus.InsufficientData), row.Cells[8].Text);
        Assert.Equal(CellTone.Warning, row.Cells[8].Tone);
    }

    [Fact]
    public void Failed_capture_shows_status_and_detail_only()
    {
        var series = SampleSeries.Full() with
        {
            Status = FrameCaptureStatus.ElevationDeclined, Detail = "UAC declined", Runs = [], Summary = null, Bottleneck = null, Recommendations = [],
        };

        var section = Series(series);

        Assert.Equal(CellTone.Warning, Field(section, ReportStrings.BenchmarkStatus).Tone);
        Assert.Equal("UAC declined", Field(section, ReportStrings.BenchmarkDetail).Text);
        Assert.DoesNotContain(section.Blocks.OfType<ReportTable>(), t => t.Headers.Contains(ReportStrings.ColumnRun));
    }

    [Fact]
    public void Single_valid_run_adds_repeat_caveat()
    {
        const string Caveat = "Годных прогонов: 1, для выводов нужно не меньше 2. Один короткий прогон недостаточен — повторите серию.";
        var series = SampleSeries.Full();

        Assert.Contains(Paragraphs(Series(series)), p => p.Text == Caveat && p.Tone == CellTone.Note);
        Assert.DoesNotContain(Paragraphs(Series(series with { Summary = series.Summary! with { ValidRuns = 2 } })), p => p.Text == Caveat);
    }

    [Fact]
    public void Series_without_valid_runs_says_there_is_no_summary_or_analysis()
    {
        var section = Series(SampleSeries.Full() with { Summary = null, Bottleneck = null });

        Assert.Contains(Paragraphs(section), p => p.Text == ReportStrings.BenchmarkNoValidRuns);
        Assert.Contains(Paragraphs(section), p => p.Text == ReportStrings.BenchmarkNoAnalysis);
        Assert.DoesNotContain(section.Blocks.OfType<ReportTable>(), t => t.Headers.Contains(ReportStrings.ColumnMedian));
    }

    [Fact]
    public void Missing_series_says_none_yet()
    {
        var section = PerformanceReport.BuildSeries(null, Russian);

        Assert.Equal(ReportSectionIds.Benchmark, section.Id);
        Assert.Equal(ReportStrings.BenchmarkNoSeries, Assert.Single(Paragraphs(section)).Text);
    }

    [Fact]
    public void Healthy_series_says_no_bottleneck_found()
    {
        var series = SampleSeries.Full() with { Bottleneck = new BottleneckAssessment([], GpuLoadState.Normal, []) };

        var section = Series(series);

        Assert.Contains(Paragraphs(section), p => p.Text == ReportStrings.BenchmarkNoBottleneck);
        Assert.DoesNotContain(section.Blocks.OfType<ReportTable>(), t => t.Headers.Contains(ReportStrings.ColumnSupports));
    }

    [Fact]
    public void Comparison_summary_sentence_for_improved_fps_and_unchanged_low()
    {
        var baseline = Valid("a", 0, new MetricSpread(100, 99, 101), new MetricSpread(60, 59, 61));
        var candidate = Valid("b", 10, new MetricSpread(107.9, 107, 109), new MetricSpread(60.5, 59.5, 61.5));
        var comparison = BenchmarkComparer.Compare(baseline, candidate, Settings);

        var section = PerformanceReport.BuildComparison(Report(candidate, comparison), Russian);

        Assert.NotNull(section);
        Assert.Equal(ReportSectionIds.Comparison, section.Id);
        Assert.Equal("Средний FPS выше в повторных тестах (+7,9%), 1% low без ухудшения.",
            Assert.IsType<ReportParagraph>(section.Blocks[0]).Content.Text);
        Assert.Equal("Новый драйвер", Field(section, ReportStrings.ComparisonChange).Text);
        var metrics = TableWith(section, ReportStrings.ColumnVerdict);
        Assert.Equal(5, metrics.Rows.Count);
        Assert.Equal([EnumText.Format(ComparedMetric.AverageFps), "100,0 (99,0–101,0)", "107,9 (107,0–109,0)", "+7,9%",
            EnumText.Format(ChangeVerdict.Improved)], metrics.Rows[0].Cells.Select(c => c.Text));
        Assert.Equal(EnumText.Format(ChangeVerdict.NoChange), metrics.Rows[1].Cells[4].Text);
    }

    [Theory]
    [InlineData(100, 107.9, 60, 55, "Средний FPS выше в повторных тестах (+7,9%), но 1% low ниже (−8,3%): плавность могла ухудшиться.")]
    [InlineData(100, 90, 60, 60, "Средний FPS ниже в повторных тестах (−10,0%).")]
    [InlineData(100, 101, 60, 60, "Изменение не дало измеримого эффекта (разница меньше 3%).")]
    [InlineData(100, 104, 60, 60, "Разница меньше разброса между прогонами — эффект не доказан.")]
    public void Comparison_summary_sentence_follows_verdicts(double fpsBefore, double fpsAfter, double lowBefore, double lowAfter, string expected)
    {
        // Прогоны ±1 FPS; в последнем случае у кандидата ±5 FPS, и +4 % остаются в пределах разброса.
        static MetricSpread Spread(double median, double half) => new(median, median - half, median + half);
        var baseline = Valid("a", 0, Spread(fpsBefore, 1), Spread(lowBefore, 0.5));
        var candidate = Valid("b", 10, Spread(fpsAfter, fpsAfter is 104 ? 5 : 1), Spread(lowAfter, 0.5));

        var section = PerformanceReport.BuildComparison(Report(candidate, BenchmarkComparer.Compare(baseline, candidate, Settings)), Russian);

        Assert.Equal(expected, Assert.IsType<ReportParagraph>(section!.Blocks[0]).Content.Text);
    }

    [Fact]
    public void Too_few_runs_and_other_game_get_their_own_sentence()
    {
        var baseline = Valid("a", 0, new MetricSpread(100, 99, 101), new MetricSpread(60, 59, 61));
        var single = Valid("b", 10, new MetricSpread(120, 119, 121), new MetricSpread(60, 59, 61));
        single = single with { Summary = single.Summary! with { ValidRuns = 1 } };
        var other = single with { Conditions = single.Conditions with { Target = "other.exe" } };

        string Sentence(BenchmarkSeries candidate) => PerformanceReport.BuildComparison(
            Report(candidate, BenchmarkComparer.Compare(baseline, candidate, Settings)), Russian)!.Blocks.OfType<ReportParagraph>().First().Content.Text;

        Assert.Equal("Повторных прогонов меньше 2 — эффект не доказан, повторите серию.", Sentence(single));
        Assert.Equal(ReportStrings.ComparisonDifferentGames, Sentence(other));
    }

    [Fact]
    public void Condition_differences_and_caveats_are_readable()
    {
        var baseline = Valid("a", 0, new MetricSpread(100, 99, 101), new MetricSpread(60, 59, 61), change: null);
        var candidate = Valid("b", 10, new MetricSpread(100, 99, 101), new MetricSpread(60, 59, 61), change: null);
        var display = candidate.Conditions.Display.Value! with { HdrEnabled = true };
        candidate = candidate with
        {
            Conditions = candidate.Conditions with
            {
                PowerSource = PowerSource.AcPower, Display = Reading<DisplayMode>.Known(display, SampleSeries.Source),
            },
        };

        var section = PerformanceReport.BuildComparison(Report(candidate, BenchmarkComparer.Compare(baseline, candidate, Settings)), Russian)!;

        Assert.Equal(ReportStrings.ComparisonChangeNotDescribed, Field(section, ReportStrings.ComparisonChange).Text);
        var differences = TableWith(section, ReportStrings.ColumnCondition);
        Assert.Contains(differences.Rows, r => r.Cells.Select(c => c.Text).SequenceEqual(
            [ReportStrings.BenchmarkConditionHdr, ReportStrings.No, ReportStrings.Yes]));
        Assert.Contains(differences.Rows, r => r.Cells.Select(c => c.Text).SequenceEqual(
            [ReportStrings.BenchmarkConditionPowerSource, EnumText.Format(PowerSource.Battery), EnumText.Format(PowerSource.AcPower)]));
        Assert.Contains(Paragraphs(section), p => p.Text == EnumText.Format(ComparisonCaveat.ConditionsDiffer) && p.Tone == CellTone.Note);
        Assert.Contains(Paragraphs(section), p => p.Text == EnumText.Format(ComparisonCaveat.ChangeNotDescribed));
    }

    [Fact]
    public void Every_condition_field_has_a_label()
    {
        foreach (var field in BenchmarkComparer.ConditionFields)
            Assert.False(string.IsNullOrEmpty(ReportStrings.ResourceManager.GetString(field, ReportStrings.Culture)), field);
    }

    [Fact]
    public void Comparison_section_absent_without_comparison()
    {
        Assert.Null(PerformanceReport.BuildComparison(Report(SampleSeries.Full()), Russian));
        Assert.Null(PerformanceReport.BuildComparison(null, Russian));
    }

    [Fact]
    public void Chain_table_lists_each_change()
    {
        var first = Valid("a", 0, new MetricSpread(100, 99, 101), new MetricSpread(60, 59, 61), change: null);
        var second = Valid("b", 10, new MetricSpread(110, 109, 111), new MetricSpread(60, 59, 61), change: "Драйвер 1");
        var third = Valid("c", 20, new MetricSpread(110.5, 109.5, 111.5), new MetricSpread(60, 59, 61), change: "Драйвер 2");
        var chain = BenchmarkComparer.Chain([third, first, second], Settings);

        var section = PerformanceReport.BuildComparison(Report(third, chain[^1], chain), Russian)!;

        Assert.Contains(section.Blocks.OfType<ReportHeading>(), h => h.Text == ReportStrings.ComparisonChainHeading);
        var table = TableWith(section, ReportStrings.ColumnResult);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(second.StartedAt.ToLocalTime().ToString("G", Russian), table.Rows[0].Cells[0].Text);
        Assert.Equal(["Драйвер 1", "Драйвер 2"], table.Rows.Select(r => r.Cells[1].Text));
        Assert.StartsWith("Средний FPS выше в повторных тестах (+10,0%)", table.Rows[0].Cells[2].Text, StringComparison.Ordinal);
        Assert.Equal("Изменение не дало измеримого эффекта (разница меньше 3%).", table.Rows[1].Cells[2].Text);
    }
}
