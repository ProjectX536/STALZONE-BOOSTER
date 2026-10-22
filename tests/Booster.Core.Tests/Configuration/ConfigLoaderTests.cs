using System.Text.Json.Nodes;
using Booster.Core.Configuration;

namespace Booster.Core.Tests.Configuration;

public class ConfigLoaderTests
{
    private const string FileName = "settings.json";

    private const string BuiltIn = """
        {
          "schemaVersion": 1,
          /* блочный комментарий */
          "monitoring": { "selfUsageIntervalMs": 2000, "gpuEngineSampleIntervalMs": 2000, "gameDetectionIntervalMs": 2000,
            "sensorIntervalMs": 2000, "gameExitConfirmationCount": 2, },
          "detectors": { "defaultTimeoutMs": 15000, "timeoutsMs": { "drivers": 90000 } },
          // строчный комментарий
          "presentMon": { "executablePath": null, "sessionName": "BoosterCapture" },
          "benchmark": { "runCount": 3, "runSeconds": 30, "pauseSeconds": 10, "startDelaySeconds": 10, "soundCues": true,
            "retainedSeriesCount": 100, "sessionCaptureSeconds": 300 },
          "logging": { "retainedFileCount": 14 },
          "journal": { "retainedSessionCount": 50 },
          "diagnostics": { "stabilityDays": 14, "maxStabilityEvents": 500, "topProcessCount": 10, "activityIntervalMs": 2000 },
          "network": { "pingCount": 30, "pingIntervalMs": 1000, "pingTimeoutMs": 1000, "host": null },
          "gameNetwork": { "measureSeconds": 180, "serverDetectSeconds": 10, "traceMaxHops": 30, "traceProbesPerHop": 3,
            "traceStopAfterSilentHops": 5, "loadIdleSeconds": 20, "loadSeconds": 30, "maxTrackedFlows": 4096 },
        }
        """;

    private static ConfigLoadResult<BoosterSettings> Load(string? userJson, params IConfigMigration[] migrations) =>
        ConfigLoader.Load<BoosterSettings>(FileName, BuiltIn, userJson, supportedVersion: 1, migrations);

    private static void AssertBuiltInValues(BoosterSettings s)
    {
        Assert.Null(s.PresentMon.ExecutablePath);
        Assert.Equal(30, s.Benchmark.RunSeconds);
        Assert.Equal(2000, s.Monitoring.SelfUsageIntervalMs);
    }

    [Fact]
    public void Built_in_json_with_comments_and_trailing_commas_is_parsed()
    {
        var result = Load(userJson: null);

        Assert.Equal(1, result.Value.SchemaVersion);
        Assert.Equal("BoosterCapture", result.Value.PresentMon.SessionName);
        Assert.Equal(90000, result.Value.Detectors.TimeoutsMs["drivers"]);
        Assert.Equal(14, result.Value.Logging.RetainedFileCount);
        Assert.Equal(180, result.Value.GameNetwork.MeasureSeconds);
    }

    [Fact]
    public void Missing_user_file_gives_built_in_without_warnings()
    {
        var result = Load(userJson: null);

        AssertBuiltInValues(result.Value);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void User_file_overrides_only_what_it_contains()
    {
        var result = Load("""{"schemaVersion":1,"presentMon":{"executablePath":"D:\\pm.exe"}}""");

        Assert.Equal(@"D:\pm.exe", result.Value.PresentMon.ExecutablePath);
        Assert.Equal(30, result.Value.Benchmark.RunSeconds);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("{ not json", ConfigWarningKind.InvalidJson)]
    [InlineData("[1, 2]", ConfigWarningKind.InvalidJson)]
    [InlineData("""{"schemaVersion":1,"benchmark":{"runSeconds":"много"}}""", ConfigWarningKind.InvalidJson)]
    [InlineData("""{"presentMon":{}}""", ConfigWarningKind.MissingSchemaVersion)]
    [InlineData("""{"schemaVersion":"1"}""", ConfigWarningKind.MissingSchemaVersion)]
    [InlineData("""{"schemaVersion":99}""", ConfigWarningKind.NewerSchemaVersion)]
    public void Unusable_user_file_falls_back_to_built_in_with_warning(string userJson, ConfigWarningKind expected)
    {
        var result = Load(userJson);

        AssertBuiltInValues(result.Value);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(expected, warning.Kind);
        Assert.Equal(FileName, warning.File);
    }

    [Fact]
    public void Older_user_file_is_migrated_before_merge()
    {
        var result = Load("""{"schemaVersion":0,"old":5}""", new RenameOldToRunSeconds());

        Assert.Equal(5, result.Value.Benchmark.RunSeconds);
        Assert.Equal(3, result.Value.Benchmark.RunCount);
        Assert.Equal("BoosterCapture", result.Value.PresentMon.SessionName);
        Assert.Equal(1, result.Value.SchemaVersion);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Old_defaultCaptureSeconds_key_in_user_settings_is_ignored()
    {
        var result = Load("""{"schemaVersion":1,"presentMon":{"executablePath":"D:\\pm.exe","defaultCaptureSeconds":5}}""");

        Assert.Equal(@"D:\pm.exe", result.Value.PresentMon.ExecutablePath);
        Assert.Equal(30, result.Value.Benchmark.RunSeconds);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Older_user_file_without_migration_falls_back_to_built_in()
    {
        var result = Load("""{"schemaVersion":0,"old":5}""");

        AssertBuiltInValues(result.Value);
        Assert.Equal(ConfigWarningKind.MigrationFailed, Assert.Single(result.Warnings).Kind);
    }

    [Fact]
    public void Throwing_migration_falls_back_to_built_in()
    {
        var result = Load("""{"schemaVersion":0,"old":5}""", new ThrowingMigration());

        AssertBuiltInValues(result.Value);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(ConfigWarningKind.MigrationFailed, warning.Kind);
        Assert.Contains("сломано", warning.Detail);
    }

    private sealed class RenameOldToRunSeconds : IConfigMigration
    {
        public int FromVersion => 0;

        public JsonObject Migrate(JsonObject document)
        {
            var old = document["old"]!.GetValue<int>();
            document.Remove("old");
            document["benchmark"] = new JsonObject { ["runSeconds"] = old };
            return document;
        }
    }

    private sealed class ThrowingMigration : IConfigMigration
    {
        public int FromVersion => 0;

        public JsonObject Migrate(JsonObject document) => throw new InvalidOperationException("сломано");
    }
}
