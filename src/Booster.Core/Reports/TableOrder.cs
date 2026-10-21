using System.Globalization;
using System.Text.RegularExpressions;
using Booster.Core.Abstractions;
using Booster.Core.Resources;

namespace Booster.Core.Reports;

public static partial class TableOrder
{
    private const double ByteBase = 1024;

    private enum Kind
    {
        Number,
        Date,
        Comparable,
        Text,
        Unknown,
    }

    private readonly record struct SortKey(Kind Kind, double Number = 0, long Ticks = 0, string Text = "", IComparable? Value = null);

    public static int Compare(object? x, object? y, bool descending, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var left = Key(x, culture);
        var right = Key(y, culture);
        if (left.Kind == Kind.Unknown || right.Kind == Kind.Unknown)
            return (left.Kind == Kind.Unknown).CompareTo(right.Kind == Kind.Unknown);

        var result = CompareKnown(left, right, culture);
        return descending ? -result : result;
    }

    public static IReadOnlyList<ReportRow> Sort(IReadOnlyList<ReportRow> rows, int column, bool descending, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentNullException.ThrowIfNull(culture);
        var comparer = Comparer<ReportCell?>.Create((x, y) => Compare(x, y, descending, culture));
        return [.. rows.OrderBy(row => column < row.Cells.Count ? row.Cells[column] : null, comparer)];
    }

    private static SortKey Key(object? value, CultureInfo culture) => value switch
    {
        null => new SortKey(Kind.Unknown),
        ReportCell { Tone: CellTone.Unavailable } => new SortKey(Kind.Unknown),
        ReportCell cell => Parse(cell.Text, culture),
        string text => Parse(text, culture),
        DateTimeOffset date => new SortKey(Kind.Date, Ticks: date.UtcTicks),
        DateTime date => new SortKey(Kind.Date, Ticks: date.Ticks),
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal =>
            new SortKey(Kind.Number, Number: Convert.ToDouble(value, CultureInfo.InvariantCulture)),
        IComparable comparable => new SortKey(Kind.Comparable, Value: comparable),
        _ => Parse(Convert.ToString(value, culture), culture),
    };

    private static int CompareKnown(SortKey x, SortKey y, CultureInfo culture)
    {
        if (x.Kind != y.Kind)
            return x.Kind.CompareTo(y.Kind);

        return x.Kind switch
        {
            Kind.Number when x.Number.CompareTo(y.Number) is var byValue and not 0 => byValue,
            Kind.Number => Natural(x.Text, y.Text, culture),
            Kind.Date => x.Ticks.CompareTo(y.Ticks),
            Kind.Comparable when x.Value!.GetType() == y.Value!.GetType() => x.Value.CompareTo(y.Value),
            Kind.Comparable => string.CompareOrdinal(x.Value!.GetType().FullName, y.Value!.GetType().FullName),
            _ => Natural(x.Text, y.Text, culture),
        };
    }

    private static SortKey Parse(string? raw, CultureInfo culture)
    {
        var text = raw?.Trim() ?? string.Empty;
        if (IsUnknown(text))
            return new SortKey(Kind.Unknown);
        if (DatePattern().IsMatch(text) && DateTime.TryParse(text, culture, DateTimeStyles.AllowWhiteSpaces, out var date))
            return new SortKey(Kind.Date, Ticks: date.Ticks);
        if (TryLeadingNumber(text, out var number, out var rest))
            return new SortKey(Kind.Number, Number: number * Scale(rest), Text: rest);
        return new SortKey(Kind.Text, Text: text);
    }

    private static bool IsUnknown(string text) =>
        text.Length == 0
        || text.All(c => c is '—' or '–' or '-')
        || string.Equals(text, ReportStrings.NotMeasured, StringComparison.OrdinalIgnoreCase)
        || Enum.GetValues<DataStatus>().Any(status => status != DataStatus.Available
            && string.Equals(text, ReadingFormatter.StatusText(status), StringComparison.OrdinalIgnoreCase));

    private static bool TryLeadingNumber(string text, out double number, out string rest)
    {
        number = 0;
        rest = text;
        var i = 0;
        var negative = text.Length > 1 && text[0] is '-' or '−' && char.IsAsciiDigit(text[1]);
        if (negative)
            i++;
        if (i >= text.Length || !char.IsAsciiDigit(text[i]))
            return false;

        var digits = new List<char>();
        while (i < text.Length)
        {
            if (char.IsAsciiDigit(text[i]))
                digits.Add(text[i++]);
            else if (IsGroupSeparator(text, i))
                i++;
            else
                break;
        }

        var fraction = string.Empty;
        if (i + 1 < text.Length && text[i] is ',' or '.' && char.IsAsciiDigit(text[i + 1]))
        {
            var end = i + 1;
            while (end < text.Length && char.IsAsciiDigit(text[end]))
                end++;
            if (end + 1 < text.Length && text[end] is ',' or '.' && char.IsAsciiDigit(text[end + 1]))
                return false;
            fraction = text[(i + 1)..end];
            i = end;
        }

        var value = double.Parse(new string([.. digits]) + (fraction.Length > 0 ? "." + fraction : string.Empty), CultureInfo.InvariantCulture);
        number = negative ? -value : value;
        rest = text[i..].TrimStart();
        return true;
    }

    // Разделитель разрядов в ru-RU — неразрывный пробел.
    private static bool IsGroupSeparator(string text, int i) =>
        text[i] is ' ' or ' '
        && i + 3 < text.Length && char.IsAsciiDigit(text[i + 1]) && char.IsAsciiDigit(text[i + 2]) && char.IsAsciiDigit(text[i + 3])
        && (i + 4 >= text.Length || !char.IsAsciiDigit(text[i + 4]));

    private static double Scale(string rest)
    {
        (string Unit, double Factor)[] units =
        [
            (ReportStrings.ByteUnitB, 1),
            (ReportStrings.ByteUnitKB, ByteBase),
            (ReportStrings.ByteUnitMB, ByteBase * ByteBase),
            (ReportStrings.ByteUnitGB, ByteBase * ByteBase * ByteBase),
            (ReportStrings.ByteUnitTB, ByteBase * ByteBase * ByteBase * ByteBase),
            (RateUnit(ReportStrings.UnitKbps), 1e3),
            (RateUnit(ReportStrings.UnitMbps), 1e6),
            (RateUnit(ReportStrings.UnitGbps), 1e9),
        ];
        foreach (var (unit, factor) in units)
        {
            if (rest.StartsWith(unit, StringComparison.Ordinal) && (rest.Length == unit.Length || !char.IsLetter(rest[unit.Length])))
                return factor;
        }
        return 1;
    }

    private static string RateUnit(string format) => string.Format(CultureInfo.InvariantCulture, format, string.Empty).Trim();

    private static int Natural(string x, string y, CultureInfo culture)
    {
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            var xDigits = char.IsAsciiDigit(x[i]);
            var yDigits = char.IsAsciiDigit(y[j]);
            var xEnd = RunEnd(x, i, xDigits);
            var yEnd = RunEnd(y, j, yDigits);
            var result = xDigits && yDigits
                ? CompareDigits(x[i..xEnd], y[j..yEnd])
                : string.Compare(x[i..xEnd], y[j..yEnd], culture, CompareOptions.IgnoreCase);
            if (result != 0)
                return result;
            i = xEnd;
            j = yEnd;
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }

    private static int RunEnd(string text, int start, bool digits)
    {
        var end = start;
        while (end < text.Length && char.IsAsciiDigit(text[end]) == digits)
            end++;
        return end;
    }

    private static int CompareDigits(string x, string y)
    {
        x = x.TrimStart('0');
        y = y.TrimStart('0');
        return x.Length != y.Length ? x.Length.CompareTo(y.Length) : string.CompareOrdinal(x, y);
    }

    [GeneratedRegex(@"^(\d{1,2}[./]\d{1,2}[./]\d{4}|\d{4}-\d{1,2}-\d{1,2})(\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();
}
