using System.Text.Json;
using System.Text.Json.Serialization;
using Booster.Core.Abstractions;

namespace Booster.Core.Serialization;

internal sealed class ReadingJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Reading<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(ReadingJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()))!;
}

internal sealed class ReadingJsonConverter<T> : JsonConverter<Reading<T>>
{
    public override Reading<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("A reading must be an object.");

        T? value = default;
        var hasValue = false;
        DataStatus? status = null;
        string? source = null;
        string? note = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "value":
                    value = reader.TokenType == JsonTokenType.Null ? default : JsonSerializer.Deserialize<T>(ref reader, options);
                    hasValue = value is not null;
                    break;
                case "status":
                    status = JsonSerializer.Deserialize<DataStatus>(ref reader, options);
                    break;
                case "source":
                    source = reader.GetString();
                    break;
                case "note":
                    note = reader.GetString();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (status is not { } known)
            throw new JsonException("A reading has no status.");
        if (known == DataStatus.Available && !hasValue)
            throw new JsonException("An available reading has no value.");
        return new Reading<T>(known == DataStatus.Available ? value : default, known, source, note);
    }

    public override void Write(Utf8JsonWriter writer, Reading<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("value");
        if (value is { IsAvailable: true, Value: { } known })
            JsonSerializer.Serialize(writer, known, options);
        else
            writer.WriteNullValue();
        writer.WriteString("status", value.Status.ToString());
        writer.WriteString("source", value.Source);
        writer.WriteString("note", value.Note);
        writer.WriteEndObject();
    }
}
