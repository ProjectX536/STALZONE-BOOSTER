using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Windows.Data;
using Booster.Core.Abstractions;
using Booster.Core.Reports;
using Booster.Core.Resources;

namespace Booster.App.Converters;

public enum ReadingPart
{
    Value,

    Source,
}

[ValueConversion(typeof(object), typeof(string))]
public sealed class ReadingTextConverter : IValueConverter
{
    public const string BytesParameter = "Bytes";

    private static readonly ConcurrentDictionary<Type, ReadingAccessor?> Accessors = new();

    public ReadingPart Part { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
            return Part == ReadingPart.Value ? ReadingFormatter.StatusText(DataStatus.Unknown) : null;

        var accessor = Accessors.GetOrAdd(value.GetType(), ReadingAccessor.TryCreate)
            ?? throw new ArgumentException($"Expected Reading<T>, got {value.GetType()}", nameof(value));

        if (Part == ReadingPart.Source)
            return ReadingFormatter.SourceText(accessor.Source(value), accessor.Note(value));

        var status = accessor.Status(value);
        if (status != DataStatus.Available)
            return ReadingFormatter.StatusText(status);
        return accessor.Value(value) is { } inner ? FormatValue(inner, parameter, culture) : ReadingFormatter.StatusText(DataStatus.Unknown);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string FormatValue(object value, object? parameter, CultureInfo culture) => value switch
    {
        long or int or uint or ulong when BytesParameter.Equals(parameter as string, StringComparison.Ordinal) =>
            ByteSizeFormatter.Format(System.Convert.ToInt64(value, CultureInfo.InvariantCulture), culture),
        Enum e => EnumText.Format(e),
        bool b => b ? ReportStrings.Yes : ReportStrings.No,
        IFormattable f => f.ToString(null, culture),
        _ => value.ToString() ?? string.Empty,
    };

    private sealed class ReadingAccessor(PropertyInfo value, PropertyInfo status, PropertyInfo source, PropertyInfo note)
    {
        public static ReadingAccessor? TryCreate(Type type)
        {
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Reading<>))
                return null;
            return new ReadingAccessor(
                type.GetProperty(nameof(Reading<object>.Value))!,
                type.GetProperty(nameof(Reading<object>.Status))!,
                type.GetProperty(nameof(Reading<object>.Source))!,
                type.GetProperty(nameof(Reading<object>.Note))!);
        }

        public object? Value(object reading) => value.GetValue(reading);

        public DataStatus Status(object reading) => (DataStatus)status.GetValue(reading)!;

        public string? Source(object reading) => (string?)source.GetValue(reading);

        public string? Note(object reading) => (string?)note.GetValue(reading);
    }
}
