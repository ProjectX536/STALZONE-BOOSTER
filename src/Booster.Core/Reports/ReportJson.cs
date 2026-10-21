using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Booster.Core.Abstractions;
using Booster.Core.Inventory;

namespace Booster.Core.Reports;

/// <summary>
/// Параметры JSON-отчёта: camelCase, перечисления строками, <see cref="Reading{T}"/> — объект
/// <c>{ value, status, source, note }</c>, LUID и PCI ID — в привычном строковом виде, отступы.
/// Числа пишутся в инвариантной культуре при любой текущей.
/// </summary>
public static class ReportJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // Кириллица остаётся читаемой. Отчёт — отдельный файл .json и в HTML не встраивается,
            // поэтому экранирование символов, опасных для HTML, не нужно.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
            Converters =
            {
                new JsonStringEnumConverter(),
                new ReadingJsonConverterFactory(),
                new ToStringJsonConverter<AdapterLuid>(),
                new ToStringJsonConverter<PciId>(),
            },
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private const string WriteOnly = "The report JSON is written for people and tools; Booster does not read it back.";

    private sealed class ReadingJsonConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Reading<>);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(ReadingJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()))!;
    }

    /// <summary>Значение пишется только у Available: у остальных статусов <see cref="Reading{T}.Value"/> не используется.</summary>
    private sealed class ReadingJsonConverter<T> : JsonConverter<Reading<T>>
    {
        public override Reading<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException(WriteOnly);

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

    private sealed class ToStringJsonConverter<T> : JsonConverter<T>
        where T : struct
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException(WriteOnly);

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
