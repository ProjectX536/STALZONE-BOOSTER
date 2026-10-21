using System.Text.Json;
using Booster.Core.Abstractions;

namespace Booster.Core.Tests.Abstractions;

public class ActionModeTests
{
    private sealed record Holder(ActionMode Mode);

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ActionModeJsonConverter() },
    };

    [Theory]
    [InlineData("allowed", ActionMode.Allowed)]
    [InlineData("optional", ActionMode.Optional)]
    [InlineData("ask-first", ActionMode.AskFirst)]
    [InlineData("recommend-only", ActionMode.RecommendOnly)]
    [InlineData("forbidden", ActionMode.Forbidden)]
    public void Config_names_map_both_ways(string text, ActionMode mode)
    {
        Assert.True(ActionModeText.TryParse(text, out var parsed));
        Assert.Equal(mode, parsed);
        Assert.Equal(text, ActionModeText.ToConfigString(mode));
    }

    [Fact]
    public void TryParse_ignores_case_and_surrounding_spaces()
    {
        Assert.True(ActionModeText.TryParse(" Ask-First ", out var mode));
        Assert.Equal(ActionMode.AskFirst, mode);
    }

    [Theory]
    [InlineData("askfirst")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_rejects_unknown_text(string? text)
    {
        Assert.False(ActionModeText.TryParse(text, out _));
    }

    [Fact]
    public void Json_reads_config_name()
    {
        var holder = JsonSerializer.Deserialize<Holder>("""{"mode":"ask-first"}""", Options);

        Assert.Equal(ActionMode.AskFirst, holder!.Mode);
    }

    [Fact]
    public void Json_rejects_unknown_name()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Holder>("""{"mode":"bogus"}""", Options));
    }

    [Fact]
    public void Json_writes_config_name()
    {
        Assert.Equal("\"ask-first\"", JsonSerializer.Serialize(ActionMode.AskFirst, Options));
    }
}
