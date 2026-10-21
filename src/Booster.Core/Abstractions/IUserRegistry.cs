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

/// <summary>Значение реестра в текстовой форме: снимок пишется в журнал JSON, а равенство записей сравнивает значения.</summary>
/// <param name="Data">DWord — десятичное число; String — как есть; Binary — hex в нижнем регистре без разделителей.</param>
public sealed record UserRegistryValue(
    [property: JsonPropertyName("kind")] UserRegistryValueKind Kind,
    [property: JsonPropertyName("data")] string Data)
{
    public static UserRegistryValue FromDWord(uint value) =>
        new(UserRegistryValueKind.DWord, value.ToString(CultureInfo.InvariantCulture));

    public static UserRegistryValue FromString(string value) => new(UserRegistryValueKind.String, value);

    public static UserRegistryValue FromBinary(ReadOnlySpan<byte> value) =>
        new(UserRegistryValueKind.Binary, Convert.ToHexStringLower(value));

    /// <summary>Байты значения Binary; у других типов — InvalidOperationException.</summary>
    public byte[] ToBytes() => Kind == UserRegistryValueKind.Binary
        ? Convert.FromHexString(Data)
        : throw new InvalidOperationException($"Registry value of kind {Kind} has no bytes");
}

/// <summary>Реестр текущего пользователя (HKCU, 64-битное представление). Только ключи из списка реализации.</summary>
public interface IUserRegistry
{
    /// <summary>null — нет ключа или значения; значение другого типа — InvalidDataException.</summary>
    UserRegistryValue? Read(string subKey, string valueName);

    /// <summary>Создаёт ключ при необходимости. Ключ вне списка — UnauthorizedAccessException до открытия.</summary>
    void Write(string subKey, string valueName, UserRegistryValue value);

    /// <summary>Нет значения — ничего не делает. Ключ вне списка — UnauthorizedAccessException до открытия.</summary>
    void Delete(string subKey, string valueName);
}
