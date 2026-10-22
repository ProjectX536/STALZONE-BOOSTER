using System.Globalization;
using System.Text;
using System.Text.Json;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Hags;
using Booster.Core.Inventory;
using Booster.Core.Metrics;
using Booster.Core.Reports;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;
using static Booster.Core.Tests.TestSupport.SnapshotFactory;

namespace Booster.Core.Tests.Reports;

public class JsonReportExporterTests
{
    private static async Task<string> ExportAsync(SystemReport report)
    {
        using var stream = new MemoryStream();
        await new JsonReportExporter().ExportAsync(report, stream, TestContext.Current.CancellationToken);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static JsonElement At(JsonDocument document, params string[] names)
    {
        var element = document.RootElement;
        foreach (var name in names)
            element = int.TryParse(name, CultureInfo.InvariantCulture, out var index) ? element[index] : element.GetProperty(name);
        return element;
    }

    [Fact]
    public void Format_is_json()
    {
        Assert.Equal("json", new JsonReportExporter().Format);
    }

    [Fact]
    public async Task Reading_is_an_object_with_value_status_source_and_note()
    {
        using var json = JsonDocument.Parse(await ExportAsync(Report()));

        var dualChannel = At(json, "snapshot", "hardware", "memory", "value", "dualChannel");

        Assert.Equal(new[] { "value", "status", "source", "note" }, dualChannel.EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.Null, dualChannel.GetProperty("value").ValueKind);
        Assert.Equal("Unknown", dualChannel.GetProperty("status").GetString());
        Assert.Equal(Source, dualChannel.GetProperty("source").GetString());
        Assert.Equal("not measured", dualChannel.GetProperty("note").GetString());
    }

    [Fact]
    public async Task Value_of_unavailable_reading_is_not_written()
    {
        var snapshot = Healthy();
        snapshot = snapshot with { Hardware = snapshot.Hardware with { Os = new(snapshot.Hardware.Os.Value, DataStatus.Failed, "WMI", "x") } };

        using var json = JsonDocument.Parse(await ExportAsync(Report(snapshot)));

        Assert.Equal(JsonValueKind.Null, At(json, "snapshot", "hardware", "os", "value").ValueKind);
    }

    [Fact]
    public async Task Enums_are_written_as_names()
    {
        using var json = JsonDocument.Parse(await ExportAsync(Report()));

        Assert.Equal("Discrete", At(json, "snapshot", "hardware", "gpu", "value", "adapters", "0", "kind", "value").GetString());
        Assert.Equal("Nvidia", At(json, "snapshot", "hardware", "gpu", "value", "adapters", "0", "vendor").GetString());
        Assert.Equal("Available", At(json, "snapshot", "hardware", "gpu", "status").GetString());
    }

    [Fact]
    public async Task Cyrillic_is_written_as_is()
    {
        var text = await ExportAsync(Report(Healthy().WithDevices(Device("Видеоадаптер"))));

        Assert.Contains("\"Видеоадаптер\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u04", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Numbers_use_invariant_culture_under_russian_locale()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        string text;
        try
        {
            text = await ExportAsync(Report());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        using var json = JsonDocument.Parse(text);
        Assert.Equal("143.856", At(json, "snapshot", "hardware", "displays", "value", "0", "refreshRateHz", "value").GetRawText());
        Assert.DoesNotContain("143,856", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Metadata_and_privileges_are_written()
    {
        using var json = JsonDocument.Parse(await ExportAsync(Report()));

        Assert.Equal(ReportMetadata.CurrentSchemaVersion, At(json, "metadata", "schemaVersion").GetInt32());
        Assert.True(At(json, "metadata", "privileges", "isElevated").GetBoolean());
    }

    [Fact]
    public async Task Identifiers_are_written_in_their_usual_form()
    {
        using var json = JsonDocument.Parse(await ExportAsync(Report()));
        var adapter = At(json, "snapshot", "hardware", "gpu", "value", "adapters", "0");

        Assert.Equal("0x00000000_0x0000C2A1", adapter.GetProperty("luid").GetProperty("value").GetString());
        Assert.Equal("VEN_10DE&DEV_2489&SUBSYS_88AC1462&REV_A1", adapter.GetProperty("pci").GetProperty("value").GetString());
    }

    [Fact]
    public async Task Json_report_has_schema_5_and_benchmark_object()
    {
        var settings = FakeConfiguration.DefaultThresholds.Comparison;
        var series = SampleSeries.Full();
        var candidate = SampleSeries.Full("b", minutes: 10);
        var benchmark = new BenchmarkReport(candidate, BenchmarkComparer.Compare(series, candidate, settings), [], settings);
        var finding = new Finding(FindingKind.LowDiskSpace, FindingSeverity.Warning, "C:", "5.0% free, threshold 10%");

        using var json = JsonDocument.Parse(await ExportAsync(Report(findings: [finding], benchmark: benchmark)));

        Assert.Equal(5, At(json, "metadata", "schemaVersion").GetInt32());
        Assert.Equal("LowDiskSpace", At(json, "findings", "0", "kind").GetString());
        Assert.Equal("game.exe", At(json, "benchmark", "series", "conditions", "target").GetString());
        Assert.Equal("InsufficientData", At(json, "benchmark", "series", "runs", "1", "metrics", "status").GetString());
        Assert.Equal("GpuLimited", At(json, "benchmark", "series", "bottleneck", "findings", "0", "class").GetString());
        Assert.Equal("b", At(json, "benchmark", "comparison", "candidateId").GetString());
        Assert.Equal(2, At(json, "benchmark", "settings", "minimumRuns").GetInt32());
        Assert.False(json.RootElement.TryGetProperty("lastMeasurement", out _));
    }

    [Fact]
    public async Task Json_report_has_system_section_advice_and_network_test()
    {
        var advice = new SystemAdvice(AdviceId.PagefileDisabled, AdviceModule.Memory, FindingSeverity.Warning, SafetyLevel.L3ManualOnly, []);
        var gateway = new NetworkTargetResult(NetworkTargetKind.Gateway, "192.168.1.1", 20, 20,
            Known(1.5), Known(0.4), Known(0.0));
        var network = new NetworkTestResult(TakenAt, TimeSpan.FromSeconds(10), null, [gateway], Reading<double>.Unknown(note: "no adapter"));

        using var json = JsonDocument.Parse(await ExportAsync(Report(advice: [advice], network: network)));

        Assert.Equal("Available", At(json, "snapshot", "system", "pagefile", "status").GetString());
        Assert.Equal("PagefileDisabled", At(json, "advice", "0", "id").GetString());
        Assert.Equal("L3ManualOnly", At(json, "advice", "0", "level").GetString());
        Assert.Equal("Gateway", At(json, "network", "targets", "0", "kind").GetString());
        Assert.Equal(1.5, At(json, "network", "targets", "0", "medianMs", "value").GetDouble());
    }

    [Fact]
    public async Task Report_contains_startup_gaming_and_tweaks()
    {
        const string path = @"C:\Users\Player\AppData\Local\Discord\Discord.exe";
        var healthy = Healthy();
        var snapshot = healthy with
        {
            System = healthy.System with
            {
                Startup = Known<IReadOnlyList<StartupItem>>(
                    [new StartupItem(new StartupEntry("Discord", StartupLocation.UserRun, $"\"{path}\"", path, StartupState.Enabled), 300L << 20)]),
                Gaming = Known(new GamingSettings(Known(false), Reading<bool>.Unknown(Source, "value absent"))),
            },
        };
        var tweak = new TweakProposal(FakeConfiguration.DefaultCatalog.RegistryTweaks[0], TweakState.Proposed, "AutoGameModeEnabled", null, []);

        using var json = JsonDocument.Parse(await ExportAsync(Report(snapshot, tweaks: [tweak])));

        Assert.Equal("UserRun", At(json, "snapshot", "system", "startup", "value", "0", "entry", "location").GetString());
        Assert.Equal(300L << 20, At(json, "snapshot", "system", "startup", "value", "0", "privateBytes").GetInt64());
        Assert.False(At(json, "snapshot", "system", "gaming", "value", "gameMode", "value").GetBoolean());
        Assert.Equal("Unknown", At(json, "snapshot", "system", "gaming", "value", "backgroundRecording", "status").GetString());
        Assert.Equal("game-mode", At(json, "tweaks", "0", "definition", "id").GetString());
        Assert.Equal("Proposed", At(json, "tweaks", "0", "state").GetString());
        Assert.Equal("AutoGameModeEnabled", At(json, "tweaks", "0", "valueName").GetString());
        Assert.Equal(5, At(json, "metadata", "schemaVersion").GetInt32());
    }

    [Fact]
    public async Task Report_contains_hags_experiment()
    {
        var experiment = new HagsExperiment(HagsExperiment.CurrentSchemaVersion, TakenAt.AddHours(-1), "game.exe", "base", BaselineEnabled: true,
            HagsExperimentStage.AwaitingSwitch, null, null, TakenAt.AddHours(-1));
        var view = new HagsExperimentView(experiment, HagsCheck.WaitingForSwitch, Reading<bool>.Known(true, Source), null, null);

        using var json = JsonDocument.Parse(await ExportAsync(Report(hags: view)));
        using var without = JsonDocument.Parse(await ExportAsync(Report()));

        Assert.True(At(json, "snapshot", "system", "hags", "value").GetBoolean());
        Assert.Equal("WaitingForSwitch", At(json, "hags", "check").GetString());
        Assert.Equal("AwaitingSwitch", At(json, "hags", "experiment", "stage").GetString());
        Assert.Equal("base", At(json, "hags", "experiment", "baselineSeriesId").GetString());
        Assert.True(At(json, "hags", "current", "value").GetBoolean());
        Assert.Equal(JsonValueKind.Null, At(json, "hags", "verdict").ValueKind);
        Assert.Equal(JsonValueKind.Null, At(without, "hags").ValueKind);
        Assert.Equal(5, At(json, "metadata", "schemaVersion").GetInt32());
    }

    [Fact]
    public async Task Json_report_without_network_test_has_null_network()
    {
        using var json = JsonDocument.Parse(await ExportAsync(Report()));

        Assert.Equal(JsonValueKind.Null, At(json, "network").ValueKind);
        Assert.Equal(0, At(json, "advice").GetArrayLength());
    }

    [Fact]
    public async Task Output_stream_stays_open()
    {
        using var stream = new MemoryStream();

        await new JsonReportExporter().ExportAsync(Report(), stream, TestContext.Current.CancellationToken);

        Assert.True(stream.CanWrite);
    }

    [Fact]
    public void Report_schema_is_5()
    {
        Assert.Equal(5, ReportMetadata.CurrentSchemaVersion);
    }

    [Fact]
    public async Task Json_report_contains_game_network_section()
    {
        var measurement = GameNetworkSamples.Measurement();
        var view = GameNetworkReportView.From(measurement, GameNetworkSamples.Analysis(measurement), GameNetworkSamples.Load(), null);

        using var json = JsonDocument.Parse(await ExportAsync(Report(gameNetwork: view)));
        using var without = JsonDocument.Parse(await ExportAsync(Report()));

        Assert.Equal(GameNetworkSamples.Server, At(json, "gameNetwork", "server").GetString());
        Assert.Equal("Etw", At(json, "gameNetwork", "serverSource").GetString());
        Assert.Equal(2, At(json, "gameNetwork", "breakdown", "homeMs", "value").GetDouble());
        Assert.Equal(2, At(json, "gameNetwork", "spikes", "beyondGateway").GetInt32());
        Assert.Equal("Cgnat", At(json, "gameNetwork", "route", "1", "scope").GetString());
        Assert.Equal("D", At(json, "gameNetwork", "load", "grade").GetString());
        Assert.Equal("steam", At(json, "gameNetwork", "consumers", "value", "0", "name").GetString());
        Assert.Equal(JsonValueKind.Null, At(without, "gameNetwork").ValueKind);
    }
}
