using System.Text.Json.Nodes;
using Booster.Core.Configuration;
using Booster.Core.Tests.TestSupport;
using Booster.Platform.Windows.Common;
using Microsoft.Extensions.Logging;

namespace Booster.Platform.Windows.Tests.Common;

public class BoosterConfigurationTests
{
    private sealed class InMemoryUserFiles : IConfigFileSource
    {
        public Dictionary<string, string> User { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Unreadable { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string ReadBuiltIn(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config", fileName));

        public string? ReadUser(string fileName) =>
            Unreadable.Contains(fileName) ? throw new IOException("locked by another process") : User.GetValueOrDefault(fileName);

        public Task WriteUserAsync(string fileName, string json, CancellationToken ct)
        {
            User[fileName] = json;
            return Task.CompletedTask;
        }
    }

    private readonly InMemoryUserFiles _files = new();
    private readonly ListLogger<BoosterConfiguration> _logger = new();

    private BoosterConfiguration Create() => new(_files, _logger);

    [Fact]
    public void Without_user_files_built_in_values_are_used()
    {
        var current = Create().Current;

        Assert.Empty(current.Warnings);
        Assert.Equal(2000, current.Settings.Monitoring.SelfUsageIntervalMs);
        Assert.Equal(300, current.Thresholds.MinimumFramesForMetrics);
        Assert.Contains("nvidia.com", current.Sources.AllowedHosts);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void Too_frequent_user_interval_is_clamped_and_reported()
    {
        _files.User[ConfigFiles.Settings] = """{"schemaVersion":1,"monitoring":{"selfUsageIntervalMs":200}}""";

        var current = Create().Current;

        Assert.Equal(1000, current.Settings.Monitoring.SelfUsageIntervalMs);
        Assert.Equal(ConfigWarningKind.ValueClamped, Assert.Single(current.Warnings).Kind);
        Assert.Equal(LogLevel.Warning, Assert.Single(_logger.Entries).Level);
    }

    [Fact]
    public void Out_of_range_user_throttle_threshold_is_clamped_and_reported()
    {
        _files.User[ConfigFiles.Thresholds] = """{"schemaVersion":1,"throttle":{"gpuBusyUtilizationPercent":30}}""";

        var current = Create().Current;

        Assert.Equal(50, current.Thresholds.Throttle.GpuBusyUtilizationPercent);
        var warning = Assert.Single(current.Warnings);
        Assert.Equal((ConfigWarningKind.ValueClamped, ConfigFiles.Thresholds), (warning.Kind, warning.File));
    }

    [Fact]
    public void Broken_user_thresholds_fall_back_with_warning_for_that_file()
    {
        _files.User[ConfigFiles.Thresholds] = "{ broken";

        var current = Create().Current;

        var warning = Assert.Single(current.Warnings);
        Assert.Equal(ConfigWarningKind.InvalidJson, warning.Kind);
        Assert.Equal(ConfigFiles.Thresholds, warning.File);
        Assert.Equal(300, current.Thresholds.MinimumFramesForMetrics);
    }

    [Fact]
    public void User_system_catalog_is_ignored_with_warning()
    {
        _files.User[ConfigFiles.SystemCatalog] = """
            {"schemaVersion":1,"services":{"systemCritical":[],"security":[],"securityKeywords":[],"antiCheat":[],
             "antiCheatKeywords":[],"deviceKeywords":[]}}
            """;

        var current = Create().Current;

        Assert.Contains("WinDefend", current.Catalog.Services.Security);
        Assert.Contains("RpcSs", current.Catalog.Services.SystemCritical);
        var warning = Assert.Single(current.Warnings);
        Assert.Equal((ConfigWarningKind.UserFileIgnored, ConfigFiles.SystemCatalog), (warning.Kind, warning.File));
        Assert.Equal(LogLevel.Warning, Assert.Single(_logger.Entries).Level);
    }

    [Fact]
    public void Built_in_system_catalog_is_loaded()
    {
        var current = Create().Current;

        Assert.Equal(SystemCatalog.CurrentSchemaVersion, current.Catalog.SchemaVersion);
        Assert.Contains("EasyAntiCheat", current.Catalog.Services.AntiCheat);
    }

    [Fact]
    public void Unreadable_user_file_falls_back_to_built_in_instead_of_failing_start()
    {
        _files.Unreadable.Add(ConfigFiles.Settings);

        var current = Create().Current;

        var warning = Assert.Single(current.Warnings);
        Assert.Equal(ConfigWarningKind.Unreadable, warning.Kind);
        Assert.Equal(ConfigFiles.Settings, warning.File);
        Assert.Contains("locked by another process", warning.Detail);
        Assert.Equal(2000, current.Settings.Monitoring.SelfUsageIntervalMs);
    }

    [Fact]
    public async Task Saving_unchanged_settings_writes_only_schema_version()
    {
        var config = Create();

        await config.SaveUserSettingsAsync(config.Current.Settings, TestContext.Current.CancellationToken);

        var written = JsonNode.Parse(_files.User[ConfigFiles.Settings]);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"schemaVersion":1}"""), written), written!.ToJsonString());
    }

    [Fact]
    public async Task Saved_settings_contain_only_changes_and_are_applied()
    {
        var config = Create();
        var changed = config.Current.Settings with
        {
            PresentMon = config.Current.Settings.PresentMon with { ExecutablePath = @"D:\Инструменты\PresentMon.exe" },
        };

        await config.SaveUserSettingsAsync(changed, TestContext.Current.CancellationToken);

        Assert.Equal(@"D:\Инструменты\PresentMon.exe", config.Current.Settings.PresentMon.ExecutablePath);
        var written = JsonNode.Parse(_files.User[ConfigFiles.Settings])!.AsObject();
        var expected = JsonNode.Parse("""{"schemaVersion":1,"presentMon":{"executablePath":"D:\\Инструменты\\PresentMon.exe"}}""");
        Assert.True(JsonNode.DeepEquals(expected, written), written.ToJsonString());
        Assert.Contains("Инструменты", _files.User[ConfigFiles.Settings]);
    }
}
