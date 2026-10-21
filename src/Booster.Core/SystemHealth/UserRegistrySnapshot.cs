using System.Text.Json;
using System.Text.Json.Serialization;
using Booster.Core.Abstractions;

namespace Booster.Core.SystemHealth;

/// <summary>Значение снимка изменения в HKCU: <c>{"subKey": "…", "valueName": "…", "value": {"kind": "DWord", "data": "1"} | null}</c>.</summary>
/// <param name="Value">null — значения не было: восстановление удаляет его.</param>
public sealed record UserRegistrySnapshot(
    [property: JsonPropertyName("subKey")] string SubKey,
    [property: JsonPropertyName("valueName")] string ValueName,
    [property: JsonPropertyName("value")] UserRegistryValue? Value)
{
    public const int SchemaVersion = 1;

    public JsonElement ToJson() => JsonSerializer.SerializeToElement(this);

    public static UserRegistrySnapshot Read(JsonElement value) =>
        value.Deserialize<UserRegistrySnapshot>() ?? throw new JsonException("user registry snapshot value is null");

    /// <summary>Снимок одного значения: исходное и применённое лежат в одном ключе под одним именем.</summary>
    public static ActionSnapshot Capture(string actionId, string subKey, string valueName, UserRegistryValue? original,
        UserRegistryValue applied, DateTimeOffset at) =>
        new(SchemaVersion, actionId, new UserRegistrySnapshot(subKey, valueName, original).ToJson(),
            new UserRegistrySnapshot(subKey, valueName, applied).ToJson(), at);
}
