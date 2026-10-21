using System.Text.Json;
using System.Text.Json.Nodes;
using Booster.Core.Configuration;
using Booster.Core.Profiles;
using Booster.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booster.Core.Tests.Profiles;

public class ProfileRepositoryTests
{
    private const string FileName = "Competitive.json";

    private readonly InMemoryProfileFileSource _files = InMemoryProfileFileSource.WithShippedCompetitive();

    private ProfileRepository Repository(ILogger<ProfileRepository>? logger = null) =>
        new(_files, logger ?? NullLogger<ProfileRepository>.Instance);

    [Fact]
    public void Built_in_profile_is_loaded_alone()
    {
        var set = Repository().Current;

        var profile = Assert.Single(set.Profiles);
        Assert.Equal("competitive", profile.ProfileId);
        Assert.Empty(profile.Games);
        Assert.Empty(set.Warnings);
        Assert.Same(profile, set.Find("competitive"));
        Assert.Null(set.Find("other"));
    }

    [Fact]
    public void User_file_sets_games()
    {
        _files.PutUser(FileName, """{ "schemaVersion": 1, "games": ["cs2.exe"] }""");

        var profile = Assert.Single(Repository().Current.Profiles);

        Assert.Equal(["cs2"], profile.Games);
        Assert.Equal(ActionIds.PowerPlan, profile.Actions[0].Id);
    }

    [Fact]
    public void User_file_with_newer_schema_falls_back_to_built_in_with_warning()
    {
        _files.PutUser(FileName, """{ "schemaVersion": 2, "games": ["cs2"] }""");
        var logger = new ListLogger<ProfileRepository>();

        var set = Repository(logger).Current;

        Assert.Empty(Assert.Single(set.Profiles).Games);
        Assert.Equal(ConfigWarningKind.NewerSchemaVersion, Assert.Single(set.Warnings).Kind);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void Unreadable_user_file_falls_back_to_built_in_with_warning()
    {
        _files.UnreadableUser.Add(FileName);

        var set = Repository().Current;

        Assert.Single(set.Profiles);
        Assert.Equal(ConfigWarningKind.Unreadable, Assert.Single(set.Warnings).Kind);
    }

    [Fact]
    public void Normalizer_warnings_are_reported()
    {
        _files.PutUser(FileName, """{ "schemaVersion": 1, "monitoring": { "sampleIntervalMs": 100 } }""");

        var set = Repository().Current;

        Assert.Equal(1000, Assert.Single(set.Profiles).Monitoring.SampleIntervalMs);
        Assert.Equal(ConfigWarningKind.ValueClamped, Assert.Single(set.Warnings).Kind);
    }

    [Fact]
    public async Task Saving_user_choices_writes_only_differences_and_reloads()
    {
        var repository = Repository();
        var changed = 0;
        repository.Changed += (_, _) => changed++;

        await repository.SaveUserChoicesAsync("competitive", new ProfileUserChoices(["cs2"], true, [], []),
            TestContext.Current.CancellationToken);

        var written = JsonNode.Parse(_files.User(FileName)!);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"schemaVersion":1,"games":["cs2"],"autoStart":true}"""), written),
            written?.ToJsonString());
        var profile = repository.Current.Find("competitive")!;
        Assert.Equal(["cs2"], profile.Games);
        Assert.True(profile.AutoStart);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task Saving_keeps_other_user_values()
    {
        _files.PutUser(FileName, """{ "schemaVersion": 1, "monitoring": { "sampleIntervalMs": 5000 } }""");
        var repository = Repository();

        await repository.SaveUserChoicesAsync("competitive",
            new ProfileUserChoices([], false, [ActionIds.GamePriority], ["chrome"]), TestContext.Current.CancellationToken);

        var profile = repository.Current.Find("competitive")!;
        Assert.Equal(5000, profile.Monitoring.SampleIntervalMs);
        Assert.Equal([ActionIds.GamePriority], profile.EnabledOptionalActions);
        Assert.Equal(["chrome"], profile.BackgroundApps.UserSelected);
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 2, "games": ["cs2"] }""")]
    [InlineData("{ not json")]
    public async Task Saving_does_not_overwrite_a_rejected_user_file(string userJson)
    {
        _files.PutUser(FileName, userJson);
        var repository = Repository();
        var changed = 0;
        repository.Changed += (_, _) => changed++;

        var ex = await Assert.ThrowsAsync<ProfileFileRejectedException>(() => repository.SaveUserChoicesAsync("competitive",
            new ProfileUserChoices(["cs2"], true, [], []), TestContext.Current.CancellationToken));

        Assert.Equal(FileName, ex.FileName);
        Assert.Equal(userJson, _files.User(FileName));
        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task Saving_over_a_user_file_with_cleaned_values_is_allowed()
    {
        // Нормализатор поправил значение, но файл применён: сохранение его дополняет.
        _files.PutUser(FileName, """{ "schemaVersion": 1, "monitoring": { "sampleIntervalMs": 100 } }""");
        var repository = Repository();

        await repository.SaveUserChoicesAsync("competitive", new ProfileUserChoices(["cs2"], false, [], []),
            TestContext.Current.CancellationToken);

        Assert.Equal(["cs2"], repository.Current.Find("competitive")!.Games);
    }

    [Fact]
    public async Task Saving_unknown_profile_throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Repository().SaveUserChoicesAsync("other",
            new ProfileUserChoices([], false, [], []), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void User_file_without_built_in_pair_is_not_loaded()
    {
        _files.PutUser("Other.json", ProfileFactory.ShippedCompetitiveJson().Replace("\"competitive\"", "\"other\"", StringComparison.Ordinal));

        var set = Repository().Current;

        Assert.Equal("competitive", Assert.Single(set.Profiles).ProfileId);
        Assert.DoesNotContain("Other.json", _files.UserReads);
    }

    [Fact]
    public void Duplicate_profile_id_is_skipped_with_warning()
    {
        _files.PutBuiltIn("Copy.json", ProfileFactory.ShippedCompetitiveJson());

        var set = Repository().Current;

        Assert.Single(set.Profiles);
        var warning = Assert.Single(set.Warnings);
        Assert.Equal(ConfigWarningKind.InvalidValue, warning.Kind);
        Assert.Equal("Copy.json", warning.File);
    }

    [Fact]
    public void Broken_built_in_file_is_a_shipping_error()
    {
        _files.PutBuiltIn(FileName, "{ not json");

        Assert.ThrowsAny<JsonException>(() => Repository());
    }
}
