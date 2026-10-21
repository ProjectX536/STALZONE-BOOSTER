using System.Text.Json;
using System.Text.Json.Serialization;
using Booster.Core.Abstractions;

namespace Booster.Core.SystemHealth;

public sealed record UserRegistrySnapshot(
    [property: JsonPropertyName("subKey")] string SubKey,
    [property: JsonPropertyName("valueName")] string ValueName,
    [property: JsonPropertyName("value")] UserRegistryValue? Value)
{
    public const int SchemaVersion = 1;

    public JsonElement ToJson() => JsonSerializer.SerializeToElement(this);

    public static UserRegistrySnapshot Read(JsonElement value) =>
        value.Deserialize<UserRegistrySnapshot>() ?? throw new JsonException("user registry snapshot value is null");

    public static ActionSnapshot Capture(string actionId, string subKey, string valueName, UserRegistryValue? original,
        UserRegistryValue applied, DateTimeOffset at) =>
        new(SchemaVersion, actionId, new UserRegistrySnapshot(subKey, valueName, original).ToJson(),
            new UserRegistrySnapshot(subKey, valueName, applied).ToJson(), at);
}
