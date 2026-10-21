using System.Globalization;
using System.Text.Json.Serialization;

namespace Booster.Core.Abstractions;

[JsonConverter(typeof(JsonStringEnumConverter<UserRegistryValueKind>))]
public enum UserRegistryValueKind
{
    DWord,
    String,
    Binary,
}

public sealed record UserRegistryValue(
    [property: JsonPropertyName("kind")] UserRegistryValueKind Kind,
    [property: JsonPropertyName("data")] string Data)
{
    public static UserRegistryValue FromDWord(uint value) =>
        new(UserRegistryValueKind.DWord, value.ToString(CultureInfo.InvariantCulture));

    public static UserRegistryValue FromString(string value) => new(UserRegistryValueKind.String, value);

    public static UserRegistryValue FromBinary(ReadOnlySpan<byte> value) =>
        new(UserRegistryValueKind.Binary, Convert.ToHexStringLower(value));

    public byte[] ToBytes() => Kind == UserRegistryValueKind.Binary
        ? Convert.FromHexString(Data)
        : throw new InvalidOperationException($"Registry value of kind {Kind} has no bytes");
}

public interface IRegistryValues
{
    UserRegistryValue? Read(string subKey, string valueName);

    void Write(string subKey, string valueName, UserRegistryValue value);

    void Delete(string subKey, string valueName);
}

public interface IUserRegistry : IRegistryValues;

public interface IMachineRegistry : IRegistryValues;
