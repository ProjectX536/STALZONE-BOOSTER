using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.Drivers;
using Booster.Core.Inventory;
using Booster.Core.Metrics;
using Booster.Core.Reports;
using Booster.Core.Resources;
using Booster.Core.Sensors;
using Booster.Core.Tests.TestSupport;
using static Booster.Core.Tests.TestSupport.SnapshotFactory;

namespace Booster.Core.Tests.Reports;

public class ReportDocumentBuilderTests
{
    private const string NvidiaUrl = "https://www.nvidia.com/en-us/drivers/";

    private static readonly FakeConfiguration Configuration = new(sources: new OfficialSources(
        OfficialSources.CurrentSchemaVersion,
        ["nvidia.com"],
        [new VendorLinkRule("10DE", "NVIDIA — драйверы", NvidiaUrl)],
        [],
        "ms-settings:windowsupdate"));

    private static readonly ReportDocumentBuilder Builder = new(new OfficialSourceResolver(Configuration), Configuration);

    private static readonly CultureInfo Russian = new("ru-RU");

    private static ReportDocument Build(SystemReport report) => Builder.Build(report, Russian);

    private static ReportSection Section(ReportDocument document, string id)
    {
        var section = document.Section(id);
        Assert.NotNull(section);
        return section;
    }

    private static ReportTable DriverTable(ReportDocument document) =>
        Assert.Single(Section(document, ReportSectionIds.Drivers).Blocks.OfType<ReportTable>());

    [Fact]
    public void Sections_follow_the_html_order_and_comparison_appears_only_with_a_comparison()
    {
        var ids = Build(Report()).Sections.Select(s => s.Id);

        Assert.Equal(
            [ReportSectionIds.Header, ReportSectionIds.Findings, ReportSectionIds.Cpu, ReportSectionIds.Gpu, ReportSectionIds.Memory,
                ReportSectionIds.Board, ReportSectionIds.Os, ReportSectionIds.Storage, ReportSectionIds.Displays, ReportSectionIds.Drivers,
                ReportSectionIds.SystemMemory, ReportSectionIds.Services, ReportSectionIds.Stability, ReportSectionIds.Network,
                ReportSectionIds.Startup, ReportSectionIds.Tweaks, ReportSectionIds.Latency, ReportSectionIds.Hags, ReportSectionIds.Benchmark,
                ReportSectionIds.Capabilities],
            ids);
    }

    [Fact]
    public void Data_section_carries_the_reading_status_and_summary_sections_carry_none()
    {
        var snapshot = Healthy() with
        {
            Hardware = Healthy().Hardware with { Os = Reading<OsInfo>.Failure("WMI Win32_OperatingSystem", "access denied") },
        };

        var document = Build(Report(snapshot));

        Assert.Equal(DataStatus.Available, Section(document, ReportSectionIds.Cpu).Status);
        Assert.Equal(DataStatus.Failed, Section(document, ReportSectionIds.Os).Status);
        Assert.Null(Section(document, ReportSectionIds.Header).Status);
        Assert.Null(Section(document, ReportSectionIds.Findings).Status);
    }

    [Fact]
    public void Missing_section_value_is_one_unavailable_paragraph_with_the_source()
    {
        var snapshot = Healthy() with
        {
            Hardware = Healthy().Hardware with { Os = Reading<OsInfo>.Failure("WMI Win32_OperatingSystem", "access denied") },
        };

        var block = Assert.Single(Section(Build(Report(snapshot)), ReportSectionIds.Os).Blocks);

        var cell = Assert.IsType<ReportParagraph>(block).Content;
        Assert.Equal(CellTone.Unavailable, cell.Tone);
        Assert.Equal("WMI Win32_OperatingSystem: access denied", cell.Tooltip);
    }

    [Fact]
    public void Known_value_has_its_source_as_tooltip()
    {
        var fields = Assert.Single(Section(Build(Report()), ReportSectionIds.Board).Blocks.OfType<ReportFieldList>());

        Assert.All(fields.Fields, f => Assert.Equal(Source, f.Value.Tooltip));
        Assert.All(fields.Fields, f => Assert.Equal(CellTone.Normal, f.Value.Tone));
    }

    [Fact]
    public void Device_with_a_finding_is_flagged_and_working_device_is_not()
    {
        var broken = Device("Сломанное") with { State = Known(DeviceState.Error), ProblemCode = Known(43) };
        var unsigned = Device("Неподписанное") with { IsSigned = Known(false) };
        var working = Device("Рабочее");

        var rows = DriverTable(Build(Report(Healthy().WithDevices(broken, unsigned, working)))).Rows;

        Assert.Equal([true, true, false], rows.Select(r => r.Flagged));
    }

    [Fact]
    public void Link_cell_has_official_links_and_no_text()
    {
        var gpu = Device(GpuName, "Display", GpuHardwareId);

        var row = Assert.Single(DriverTable(Build(Report(Healthy().WithDevices(gpu)))).Rows);

        var links = row.Cells[^1];
        Assert.Equal(string.Empty, links.Text);
        Assert.Contains(links.Links, l => l.Url == new Uri(NvidiaUrl));
    }

    [Fact]
    public void Device_in_error_state_shows_the_problem_code_as_a_warning()
    {
        var broken = Device("Сломанное") with { State = Known(DeviceState.Error), ProblemCode = Known(43) };

        var state = Assert.Single(DriverTable(Build(Report(Healthy().WithDevices(broken)))).Rows).Cells[6];

        Assert.Equal(CellTone.Warning, state.Tone);
        Assert.Contains("43", state.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Benchmark_and_comparison_sections_follow_system_sections()
    {
        var series = SampleSeries.Full();
        var comparison = BenchmarkComparer.Compare(series, series with { Id = "b" }, FakeConfiguration.DefaultThresholds.Comparison);

        var document = Build(Report(benchmark: new BenchmarkReport(series, comparison, [], FakeConfiguration.DefaultThresholds.Comparison)));

        var ids = document.Sections.Select(s => s.Id).ToList();
        Assert.Equal(ids.IndexOf(ReportSectionIds.Hags) + 1, ids.IndexOf(ReportSectionIds.Benchmark));
        Assert.Equal(ids.IndexOf(ReportSectionIds.Benchmark) + 1, ids.IndexOf(ReportSectionIds.Comparison));
        Assert.Equal(ReportSectionIds.Capabilities, ids[^1]);
    }

    [Fact]
    public void Report_without_series_has_benchmark_section_and_no_comparison()
    {
        var document = Build(Report());

        var section = Section(document, ReportSectionIds.Benchmark);
        Assert.Equal(ReportStrings.BenchmarkNoSeries, Assert.IsType<ReportParagraph>(Assert.Single(section.Blocks)).Content.Text);
        Assert.Null(document.Section(ReportSectionIds.Comparison));
    }

    private const string NvidiaProvider = "providers.gpu.nvidia";

    private static SensorSnapshot Sensors(GpuTelemetry telemetry, CpuTelemetry? cpu = null) =>
        new(TakenAt, [new GpuSensorReading(SensorSamples.Gpu, NvidiaProvider, telemetry)], cpu ?? SensorSamples.Cpu());

    private static IReadOnlyList<ReportField> AllFields(ReportSection section) =>
        [.. section.Blocks.OfType<ReportFieldList>().SelectMany(l => l.Fields)];

    private static ReportCell Field(ReportSection section, string label) => Assert.Single(AllFields(section), f => f.Label == label).Value;

    private static BenchmarkReport Benchmark(ThrottleAssessment? throttle)
    {
        var series = SampleSeries.Full();
        return new BenchmarkReport(series with { Telemetry = series.Telemetry with { Throttle = throttle } }, null, [],
            FakeConfiguration.DefaultThresholds.Comparison);
    }

    private static GpuSetting Setting(string id, string name, Reading<string> value, GpuSettingOrigin origin) => new(id, name, value, origin, true);

    [Fact]
    public void Gpu_section_shows_sensor_readings_with_sources()
    {
        var telemetry = SensorSamples.Telemetry(utilization: 42, temperature: 65, temperatureLimit: 83, coreClock: 1905) with
        {
            FanRpm = Reading<int>.Known(1450, "nvapi"),
            VramUsedBytes = Reading<long>.Known(3 * GiB, "counters"),
            VramTotalBytes = Reading<long>.Known(8 * GiB, "dxgi"),
            ThrottleReasons = Reading<GpuThrottleReasons>.Known(GpuThrottleReasons.Thermal | GpuThrottleReasons.PowerLimit, "nvapi"),
        };

        var gpu = Section(Build(Report(Healthy().WithSensors(Sensors(telemetry)))), ReportSectionIds.Gpu);

        Assert.Contains(gpu.Blocks.OfType<ReportHeading>(), h => h.Text == ReportStrings.SensorsAtSnapshot);
        Assert.Equal("42 %", Field(gpu, ReportStrings.SensorUtilization).Text);
        Assert.Equal(SensorSamples.Source, Field(gpu, ReportStrings.SensorUtilization).Tooltip);
        Assert.Equal("65 °C (предел 83 °C)", Field(gpu, ReportStrings.SensorTemperature).Text);
        Assert.Equal("1905 МГц", Field(gpu, ReportStrings.SensorCoreClock).Text);
        Assert.Equal("1450 об/мин", Field(gpu, ReportStrings.SensorFan).Text);
        Assert.Equal("nvapi", Field(gpu, ReportStrings.SensorFan).Tooltip);
        Assert.Equal("3 ГБ из 8 ГБ", Field(gpu, ReportStrings.SensorVram).Text);
        Assert.Equal("температура, предел мощности", Field(gpu, ReportStrings.SensorThrottleReasons).Text);
        Assert.Equal(NvidiaProvider, Field(gpu, ReportStrings.SensorProvider).Text);
    }

    [Fact]
    public void Unknown_sensor_is_rendered_as_unknown_not_zero()
    {
        var gpu = Section(Build(Report(Healthy().WithSensors(Sensors(SensorSamples.Telemetry(utilization: null, temperature: null))))),
            ReportSectionIds.Gpu);

        var utilization = Field(gpu, ReportStrings.SensorUtilization);
        Assert.Equal(EnumText.Format(DataStatus.Unknown), utilization.Text);
        Assert.Equal(CellTone.Unavailable, utilization.Tone);
        Assert.Equal(EnumText.Format(DataStatus.Unknown), Field(gpu, ReportStrings.SensorTemperature).Text);
        Assert.Equal(EnumText.Format(DataStatus.Unknown), Field(gpu, ReportStrings.SensorPower).Text);
    }

    [Fact]
    public void Driver_settings_table_shows_origin()
    {
        var global = new GpuSettingsResult(NvidiaProvider, null, "Base Profile", Reading<IReadOnlyList<GpuSetting>>.Known([
            Setting("0x1057EB71", "Режим управления электропитанием", Reading<string>.Known("максимальная производительность", "nvapi"),
                GpuSettingOrigin.UserChanged),
            Setting("0x10835002", "Ограничение частоты кадров", Reading<string>.Unknown("nvapi", "не задан"), GpuSettingOrigin.NotSet),
            Setting("0x00A879CF", "Вертикальная синхронизация", Reading<string>.Known("определяет приложение", "nvapi"),
                GpuSettingOrigin.DriverDefault),
        ], "nvapi"));

        var gpu = Section(Build(Report(Healthy().WithSensors(Sensors(SensorSamples.Telemetry()), global))), ReportSectionIds.Gpu);

        Assert.Contains(gpu.Blocks.OfType<ReportHeading>(), h => h.Text == ReportStrings.GpuSettingsGlobal);
        var table = Assert.Single(gpu.Blocks.OfType<ReportTable>(), t => t.Headers.Contains(ReportStrings.ColumnOrigin));
        Assert.Equal([ReportStrings.ColumnSetting, ReportStrings.ColumnValue, ReportStrings.ColumnOrigin], table.Headers);
        Assert.Equal(["изменён", "не задан", "по умолчанию"], table.Rows.Select(r => r.Cells[2].Text));
        Assert.Equal("максимальная производительность", table.Rows[0].Cells[1].Text);
        Assert.Equal(CellTone.Unavailable, table.Rows[1].Cells[1].Tone);
    }

    [Fact]
    public void Application_without_profile_says_globals_apply()
    {
        var global = new GpuSettingsResult(NvidiaProvider, null, "Base Profile", Reading<IReadOnlyList<GpuSetting>>.Known([], "nvapi"));
        var game = new GpuSettingsResult(NvidiaProvider, "game.exe", null, Reading<IReadOnlyList<GpuSetting>>.Known([], "nvapi"));

        var gpu = Section(Build(Report(Healthy().WithSensors(Sensors(SensorSamples.Telemetry()), global, game))), ReportSectionIds.Gpu);

        Assert.Contains(gpu.Blocks.OfType<ReportHeading>(), h => h.Text == "Профиль приложения game.exe");
        Assert.Contains(gpu.Blocks.OfType<ReportParagraph>(), p => p.Content.Text == ReportStrings.GpuSettingsNoProfile);
    }

    [Fact]
    public void Cpu_temperature_is_marked_unsupported()
    {
        var cpu = new CpuTelemetry(Reading<double>.Known(4650.4, "counters"), Reading<double>.Known(3800, "counters"),
            Reading<double>.Known(122, "counters"), Reading<double>.Known(0, "counters"),
            Reading<double>.Unsupported("cpu", ReportStrings.CpuTemperatureNoSource));

        var section = Section(Build(Report(Healthy().WithSensors(Sensors(SensorSamples.Telemetry(), cpu)))), ReportSectionIds.Cpu);

        var temperature = Field(section, ReportStrings.CpuSensorTemperature);
        Assert.Equal(EnumText.Format(DataStatus.NotSupported), temperature.Text);
        Assert.Contains(ReportStrings.CpuTemperatureNoSource, temperature.Tooltip, StringComparison.Ordinal);
        Assert.Equal("4650 МГц", Field(section, ReportStrings.CpuEffectiveFrequency).Text);
        Assert.Equal("3800 МГц", Field(section, ReportStrings.CpuNominalFrequency).Text);
        Assert.Equal("0 %", Field(section, ReportStrings.CpuPerformanceLimit).Text);
    }

    [Fact]
    public void Benchmark_section_lists_throttle_findings()
    {
        var assessment = new ThrottleAssessment(2, 2, true, [
            new ThrottleFinding(ThrottleSignal.GpuDriverThermal, ThrottleConfidence.High, GpuName, 1, 2, Reading<double>.Known(87, "test")),
            new ThrottleFinding(ThrottleSignal.CpuPerformanceLimited, ThrottleConfidence.Low, null, 2, 2, Reading<double>.Known(15, "test")),
        ]);

        var section = Section(Build(Report(benchmark: Benchmark(assessment))), ReportSectionIds.Benchmark);

        Assert.Contains(section.Blocks.OfType<ReportHeading>(), h => h.Text == ReportStrings.ThrottleHeading);
        var findings = Assert.Single(section.Blocks.OfType<ReportTable>(), t => t.Headers.Contains(ReportStrings.ColumnSignal));
        Assert.Equal(2, findings.Rows.Count);
        Assert.Equal([EnumText.Format(ThrottleSignal.GpuDriverThermal), EnumText.Format(ThrottleConfidence.High), GpuName, "в 1 из 2 замеров", "87 °C"],
            findings.Rows[0].Cells.Select(c => c.Text));
        Assert.Equal("CPU", findings.Rows[1].Cells[2].Text);
        Assert.Equal("15 %", findings.Rows[1].Cells[4].Text);
    }

    [Fact]
    public void Healthy_series_says_no_throttling()
    {
        var section = Section(Build(Report(benchmark: Benchmark(new ThrottleAssessment(2, 2, true, [])))), ReportSectionIds.Benchmark);

        Assert.Contains(section.Blocks.OfType<ReportParagraph>(), p => p.Content.Text == ReportStrings.ThrottleNoneFound);
        Assert.DoesNotContain(section.Blocks.OfType<ReportTable>(), t => t.Headers.Contains(ReportStrings.ColumnSignal));
    }

    [Fact]
    public void Too_few_samples_say_no_conclusion_about_clocks()
    {
        var section = Section(Build(Report(benchmark: Benchmark(new ThrottleAssessment(1, 2, false, [])))), ReportSectionIds.Benchmark);

        Assert.Contains(section.Blocks.OfType<ReportParagraph>(), p => p.Content.Text == "Мало замеров (1 из нужных 2) — о частотах вывод не делается");
        Assert.DoesNotContain(section.Blocks.OfType<ReportParagraph>(), p => p.Content.Text == ReportStrings.ThrottleNoneFound);
    }

    [Fact]
    public void Schema_version_is_4() => Assert.Equal(4, ReportMetadata.CurrentSchemaVersion);
}
