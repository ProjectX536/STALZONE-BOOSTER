using System.Text.Json;
using System.Text.Json.Serialization;

namespace Booster.Core.Abstractions;

/// <summary>Режим действия в профиле. В JSON записывается строками из <see cref="ActionModeText"/>.</summary>
public enum ActionMode
{
    Allowed,
    Optional,
    AskFirst,
    RecommendOnly,
    Forbidden,
}

public static class ActionModeText
{
    private static readonly (string Text, ActionMode Mode)[] Names =
    [
        ("allowed", ActionMode.Allowed),
        ("optional", ActionMode.Optional),
        ("ask-first", ActionMode.AskFirst),
        ("recommend-only", ActionMode.RecommendOnly),
        ("forbidden", ActionMode.Forbidden),
    ];

    public static bool TryParse(string? text, out ActionMode mode)
    {
        var trimmed = text?.Trim();
        foreach (var (name, value) in Names)
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                mode = value;
                return true;
            }
        }

        mode = default;
        return false;
    }

    public static string ToConfigString(ActionMode mode)
    {
        foreach (var (name, value) in Names)
        {
            if (value == mode)
                return name;
        }

        throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
    }
}

public sealed class ActionModeJsonConverter : JsonConverter<ActionMode>
{
    public override ActionMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        return ActionModeText.TryParse(text, out var mode)
            ? mode
            : throw new JsonException($"Неизвестный режим действия: '{text}'.");
    }

    public override void Write(Utf8JsonWriter writer, ActionMode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ActionModeText.ToConfigString(value));
}
