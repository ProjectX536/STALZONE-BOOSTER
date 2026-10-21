using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Metrics;
using Booster.Core.Profiles;
using Booster.Core.Recovery;
using Booster.Platform.Windows.Monitoring;
using Booster.Platform.Windows.Providers;
using Booster.Platform.Windows.Tests.TestSupport;

namespace Booster.Platform.Windows.Tests.Scripts;

[Trait("Category", "Host")]
public class ScriptTests
{
    private static readonly string[] SectionStatuses = Enum.GetNames<DataStatus>();

    public static TheoryData<string> Collectors =>
        ["00-Collect-Hardware.ps1", "01-Collect-Drivers.ps1", "02-Collect-Performance.ps1", "03-Collect-Storage.ps1", "07-Generate-Driver-Report.ps1"];

    [Theory]
    [MemberData(nameof(Collectors))]
    public async Task Collector_prints_json_document_to_stdout(string script)
    {
        var run = await PowerShellScript.RunAsync(script);

        Assert.True(run.ExitCode == 0, run.StdErr);
        Assert.All(run.StdOut, ch => Assert.True(char.IsAscii(ch)));
        using var doc = JsonDocument.Parse(run.StdOut);
        AssertDocument(doc.RootElement, script);
        AssertNoMachineOrUserName(doc);
    }

    [Fact]
    public async Task Hardware_script_has_all_sections()
    {
        var run = await PowerShellScript.RunAsync("00-Collect-Hardware.ps1");

        Assert.True(run.ExitCode == 0, run.StdErr);
        using var doc = JsonDocument.Parse(run.StdOut);
        var sections = doc.RootElement.GetProperty("sections");
        Assert.Equal(["cpu", "gpu", "memory", "board", "bios", "os"], sections.EnumerateObject().Select(s => s.Name));
        Assert.Equal("Available", sections.GetProperty("cpu").GetProperty("status").GetString());
        Assert.Equal("Available", sections.GetProperty("os").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Export_writes_five_documents_and_summaries()
    {
        var outDir = Path.Combine(Path.GetTempPath(), $"booster-export-{Guid.NewGuid():N}");
        try
        {
            var run = await PowerShellScript.RunAsync("04-Export-Diagnostics.ps1", "-OutDir", outDir);

            Assert.True(run.ExitCode == 0, run.StdErr);
            var expected = new Dictionary<string, string>
            {
                ["hardware.json"] = "00-Collect-Hardware.ps1",
                ["drivers.json"] = "01-Collect-Drivers.ps1",
                ["performance.json"] = "02-Collect-Performance.ps1",
                ["storage.json"] = "03-Collect-Storage.ps1",
                ["driver-report.json"] = "07-Generate-Driver-Report.ps1",
            };
            Assert.Equal(expected.Keys.Order(), Directory.EnumerateFiles(outDir, "*.json").Select(Path.GetFileName).Order());
            foreach (var (file, script) in expected)
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outDir, file), TestContext.Current.CancellationToken));
                AssertDocument(doc.RootElement, script);
                AssertNoMachineOrUserName(doc);
            }
            Assert.NotEmpty(await File.ReadAllTextAsync(Path.Combine(outDir, "summary.md"), TestContext.Current.CancellationToken));
            Assert.NotEmpty(await File.ReadAllTextAsync(Path.Combine(outDir, "drivers.md"), TestContext.Current.CancellationToken));
        }
        finally
        {
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public async Task Baseline_from_real_capture_matches_frame_metrics_calculator()
    {
        var csv = FixturePath("pm26-firefox.csv");

        var run = await PowerShellScript.RunAsync("05-Run-Baseline.ps1", "-InputCsv", csv, "-ProcessName", "firefox.exe");

        Assert.True(run.ExitCode == 0, run.StdErr);
        using var doc = JsonDocument.Parse(run.StdOut);
        AssertDocument(doc.RootElement, "05-Run-Baseline.ps1");
        var metrics = Section(doc, "metrics");
        var minimumFrames = metrics.GetProperty("minimumFrames").GetInt32();
        Assert.Equal(300, minimumFrames);
        var expected = Calculate(csv, "firefox.exe", minimumFrames);
        Assert.Equal(FrameMetricsStatus.Ok, expected.Status);
        AssertMetrics(expected, metrics);
    }

    public static TheoryData<string> CsvFixtures =>
    [
        "pm26-firefox.csv", "pm-v1.synthetic.csv", "pm-v2-frametime.synthetic.csv", "pm-two-pids.synthetic.csv",
        "pm-two-swapchains.synthetic.csv", "pm-header-only.csv", "pm-unknown-columns.synthetic.csv",
    ];

    [Theory]
    [MemberData(nameof(CsvFixtures))]
    public async Task Baseline_parses_csv_like_the_application(string fixture)
    {
        var csv = FixturePath(fixture);
        var processName = fixture.StartsWith("pm26-", StringComparison.Ordinal) ? "firefox.exe" : "game.exe";

        var run = await PowerShellScript.RunAsync("05-Run-Baseline.ps1", "-InputCsv", csv, "-ProcessName", processName, "-MinimumFrames", "1");

        Assert.True(run.ExitCode == 0, run.StdErr);
        using var doc = JsonDocument.Parse(run.StdOut);
        var capture = Section(doc, "capture");
        PresentMonCsvParse parse;
        using (var reader = new StreamReader(csv))
            parse = PresentMonCsvParser.Parse(reader, processName, null, csv);
        Assert.Equal(parse.Status.ToString(), capture.GetProperty("captureStatus").GetString());
        if (parse.Capture is not { } expected)
        {
            Assert.Equal("Unknown", doc.RootElement.GetProperty("sections").GetProperty("metrics").GetProperty("status").GetString());
            return;
        }

        Assert.Equal(expected.ProcessName, capture.GetProperty("processName").GetString());
        Assert.Equal(expected.ProcessId, capture.GetProperty("processId").GetInt32());
        Assert.Equal(expected.IntervalColumn, capture.GetProperty("intervalColumn").GetString());
        Assert.Equal(expected.Frames.Count, capture.GetProperty("frames").GetInt32());
        Assert.Equal(expected.DiscardedRows, capture.GetProperty("discardedRows").GetInt32());
        Assert.Equal(expected.FrameTypes, capture.GetProperty("frameTypes").EnumerateArray().Select(t => t.GetString()));
        AssertMetrics(FrameMetricsCalculator.Calculate(expected.IntervalsMs(), 1), Section(doc, "metrics"));
    }

    [Fact]
    public async Task Verify_restore_reports_matching_state()
    {
        using var journal = new TempJournal();
        var processes = new ProcessProvider();
        var self = processes.Identify(Environment.ProcessId)!.Value;
        var session = journal.Write("20261019-120000-0000000a",
            PowerPlanChange(new PowerPlanProvider().GetActiveScheme()),
            GamePriorityChange(self, processes.GetPriority(self)!.Value),
            Change(ActionIds.BackgroundApps, ChangeStatus.NeedsManualCheck, new { apps = new[] { "chrome" } }));

        var run = await PowerShellScript.RunAsync("06-Verify-Restore.ps1", "-JournalDir", journal.Directory);

        Assert.True(run.ExitCode == 0, run.StdErr);
        Assert.All(run.StdOut, ch => Assert.True(char.IsAscii(ch)));
        using var doc = JsonDocument.Parse(run.StdOut);
        AssertDocument(doc.RootElement, "06-Verify-Restore.ps1");
        AssertNoMachineOrUserName(doc);
        var info = Section(doc, "session");
        Assert.Equal(session.SessionId, info.GetProperty("sessionId").GetString());
        Assert.Equal("Completed", info.GetProperty("outcome").GetString());
        var changes = Section(doc, "changes").EnumerateArray().ToList();
        Assert.Equal([ActionIds.PowerPlan, ActionIds.GamePriority, ActionIds.BackgroundApps], changes.Select(c => c.GetProperty("actionId").GetString()));
        Assert.True(changes[0].GetProperty("matchesOriginal").GetBoolean(), changes[0].GetRawText());
        Assert.True(changes[1].GetProperty("matchesOriginal").GetBoolean(), changes[1].GetRawText());
        Assert.Equal(JsonValueKind.Null, changes[2].GetProperty("matchesOriginal").ValueKind);
        Assert.Equal("Restored", changes[0].GetProperty("journalStatus").GetString());
        AssertSummary(doc, total: 3, matching: 2, differing: 0, notCheckable: 1);
    }

    [Fact]
    public async Task Verify_restore_reports_difference()
    {
        using var journal = new TempJournal();
        journal.Write("20261019-110000-0000000b", PowerPlanChange(new PowerPlanProvider().GetActiveScheme()));
        journal.Write("20261019-120000-0000000c", PowerPlanChange(Guid.NewGuid()));

        var run = await PowerShellScript.RunAsync("06-Verify-Restore.ps1", "-JournalDir", journal.Directory);

        Assert.True(run.ExitCode == 0, run.StdErr);
        using var doc = JsonDocument.Parse(run.StdOut);
        Assert.Equal("20261019-120000-0000000c", Section(doc, "session").GetProperty("sessionId").GetString());
        Assert.False(Section(doc, "changes")[0].GetProperty("matchesOriginal").GetBoolean());
        AssertSummary(doc, total: 1, matching: 0, differing: 1, notCheckable: 0);
    }

    [Fact]
    public async Task Verify_restore_reads_the_session_named_by_id()
    {
        using var journal = new TempJournal();
        journal.Write("20261019-110000-0000000d", PowerPlanChange(new PowerPlanProvider().GetActiveScheme()));
        journal.Write("20261019-120000-0000000e", PowerPlanChange(Guid.NewGuid()));

        var run = await PowerShellScript.RunAsync("06-Verify-Restore.ps1", "-JournalDir", journal.Directory, "-SessionId", "20261019-110000-0000000d");

        Assert.True(run.ExitCode == 0, run.StdErr);
        using var doc = JsonDocument.Parse(run.StdOut);
        Assert.Equal("20261019-110000-0000000d", Section(doc, "session").GetProperty("sessionId").GetString());
        AssertSummary(doc, total: 1, matching: 1, differing: 0, notCheckable: 0);
    }

    [Fact]
    public async Task Verify_restore_marks_gone_game_and_unknown_action()
    {
        using var journal = new TempJournal();
        var gone = new ProcessIdentity(Environment.ProcessId, DateTimeOffset.Now.AddHours(-30));
        journal.Write("20261019-120000-0000000f",
            GamePriorityChange(gone, ProcessPriority.Normal),
            Change("future-action", ChangeStatus.Restored, new { value = 1 }));

        var run = await PowerShellScript.RunAsync("06-Verify-Restore.ps1", "-JournalDir", journal.Directory);

        Assert.True(run.ExitCode == 0, run.StdErr);
        using var doc = JsonDocument.Parse(run.StdOut);
        var changes = Section(doc, "changes").EnumerateArray().ToList();
        Assert.Equal(JsonValueKind.Null, changes[0].GetProperty("current").ValueKind);
        Assert.True(changes[0].GetProperty("matchesOriginal").GetBoolean());
        Assert.Equal(JsonValueKind.Null, changes[1].GetProperty("matchesOriginal").ValueKind);
        AssertSummary(doc, total: 2, matching: 1, differing: 0, notCheckable: 1);
    }

    [Fact]
    public async Task Verify_restore_without_journal_reports_unknown()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"booster-journal-{Guid.NewGuid():N}");

        var run = await PowerShellScript.RunAsync("06-Verify-Restore.ps1", "-JournalDir", missing);

        Assert.True(run.ExitCode == 0, run.StdErr);
        Assert.All(run.StdOut, ch => Assert.True(char.IsAscii(ch)));
        using var doc = JsonDocument.Parse(run.StdOut);
        AssertDocument(doc.RootElement, "06-Verify-Restore.ps1");
        AssertNoMachineOrUserName(doc);
        Assert.Equal("Unknown", doc.RootElement.GetProperty("sections").GetProperty("session").GetProperty("status").GetString());
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public async Task Verify_restore_does_not_read_a_newer_journal_format()
    {
        using var journal = new TempJournal();
        var session = journal.Write("20261019-120000-00000010", PowerPlanChange(Guid.NewGuid()));
        var json = JsonNode.Parse(SnapshotStore.Serialize(session))!;
        json["schemaVersion"] = 2;
        File.WriteAllText(Path.Combine(journal.Directory, session.SessionId + ".json"), json.ToJsonString());

        var run = await PowerShellScript.RunAsync("06-Verify-Restore.ps1", "-JournalDir", journal.Directory);

        Assert.True(run.ExitCode == 0, run.StdErr);
        using var doc = JsonDocument.Parse(run.StdOut);
        AssertDocument(doc.RootElement, "06-Verify-Restore.ps1");
        Assert.Equal("NotSupported", doc.RootElement.GetProperty("sections").GetProperty("session").GetProperty("status").GetString());
    }

    private static void AssertSummary(JsonDocument doc, int total, int matching, int differing, int notCheckable)
    {
        var summary = Section(doc, "summary");
        Assert.Equal((total, matching, differing, notCheckable),
            (summary.GetProperty("total").GetInt32(), summary.GetProperty("matching").GetInt32(),
                summary.GetProperty("differing").GetInt32(), summary.GetProperty("notCheckable").GetInt32()));
    }

    private static ChangeEntry PowerPlanChange(Guid scheme) =>
        Change(ActionIds.PowerPlan, ChangeStatus.Restored, new { scheme, name = "Original" }, new { scheme = Guid.NewGuid(), name = "Applied" });

    private static ChangeEntry GamePriorityChange(ProcessIdentity process, ProcessPriority priority) =>
        Change(ActionIds.GamePriority, ChangeStatus.Restored,
            new { processId = process.ProcessId, startTime = process.StartTime, name = "game", priority = priority.ToString() },
            new { processId = process.ProcessId, startTime = process.StartTime, name = "game", priority = nameof(ProcessPriority.High) });

    private static ChangeEntry Change(string actionId, ChangeStatus status, object original, object? applied = null)
    {
        var snapshot = new ActionSnapshot(1, actionId, JsonSerializer.SerializeToElement(original),
            JsonSerializer.SerializeToElement(applied ?? original), DateTimeOffset.Now);
        return new ChangeEntry(actionId, actionId, actionId, actionId != ActionIds.BackgroundApps, false, status, snapshot, null, null, null,
            DateTimeOffset.Now);
    }

    private sealed class TempJournal : IDisposable
    {
        public string Directory { get; } = System.IO.Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"booster-journal-{Guid.NewGuid():N}")).FullName;

        public SessionRecord Write(string sessionId, params ChangeEntry[] changes)
        {
            var session = new SessionRecord(SessionRecord.CurrentSchemaVersion, sessionId, "competitive", "Competitive Gaming", "game", null,
                ExecutionTrigger.UserInitiated, DateTimeOffset.Now.AddHours(-1), SessionState.Idle, SessionOutcome.Completed,
                DateTimeOffset.Now, changes, []);
            File.WriteAllText(Path.Combine(Directory, sessionId + ".json"), SnapshotStore.Serialize(session));
            return session;
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "PresentMon", name);

    private static FrameMetricsResult Calculate(string csv, string processName, int minimumFrames)
    {
        using var reader = new StreamReader(csv);
        var capture = PresentMonCsvParser.Parse(reader, processName, null, csv).Capture;
        Assert.NotNull(capture);
        return FrameMetricsCalculator.Calculate(capture.IntervalsMs(), minimumFrames);
    }

    private static void AssertMetrics(FrameMetricsResult expected, JsonElement actual)
    {
        Assert.Equal(expected.Status.ToString(), actual.GetProperty("status").GetString());
        Assert.Equal(expected.ValidIntervals, actual.GetProperty("validIntervals").GetInt32());
        Assert.Equal(expected.InvalidIntervals, actual.GetProperty("invalidIntervals").GetInt32());
        if (expected.Metrics is not { } m)
        {
            Assert.Equal(JsonValueKind.Null, actual.GetProperty("metrics").ValueKind);
            return;
        }

        var metrics = actual.GetProperty("metrics");
        Assert.Equal(m.FrameCount, metrics.GetProperty("frameCount").GetInt32());
        Assert.Equal(m.DurationMs, metrics.GetProperty("durationMs").GetDouble(), 1e-9);
        Assert.Equal(m.AverageFps, metrics.GetProperty("averageFps").GetDouble(), 1e-9);
        Assert.Equal(m.OnePercentLowFps, metrics.GetProperty("onePercentLowFps").GetDouble(), 1e-9);
        Assert.Equal(m.FrameTimeP50Ms, metrics.GetProperty("frameTimeP50Ms").GetDouble(), 1e-9);
        Assert.Equal(m.FrameTimeP95Ms, metrics.GetProperty("frameTimeP95Ms").GetDouble(), 1e-9);
        Assert.Equal(m.FrameTimeP99Ms, metrics.GetProperty("frameTimeP99Ms").GetDouble(), 1e-9);
    }

    private static JsonElement Section(JsonDocument doc, string name)
    {
        var section = doc.RootElement.GetProperty("sections").GetProperty(name);
        Assert.True(section.GetProperty("status").GetString() == "Available", section.GetRawText());
        return section.GetProperty("value");
    }

    private static void AssertDocument(JsonElement root, string script)
    {
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(script, root.GetProperty("script").GetString());
        Assert.True(DateTimeOffset.TryParse(root.GetProperty("collectedAt").GetString(), out _));
        Assert.Contains(root.GetProperty("isElevated").ValueKind, (JsonValueKind[])[JsonValueKind.True, JsonValueKind.False]);
        var sections = root.GetProperty("sections").EnumerateObject().ToList();
        Assert.NotEmpty(sections);
        foreach (var section in sections)
        {
            var status = section.Value.GetProperty("status").GetString();
            Assert.Contains(status, SectionStatuses);
            Assert.True(section.Value.TryGetProperty(status == "Available" ? "value" : "note", out _), section.Value.GetRawText());
        }
    }

    private static void AssertNoMachineOrUserName(JsonDocument doc)
    {
        var text = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        foreach (var name in new[] { Environment.MachineName, Environment.UserName }.Where(n => n.Length >= 3))
            Assert.DoesNotContain(name, text, StringComparison.OrdinalIgnoreCase);
    }
}
