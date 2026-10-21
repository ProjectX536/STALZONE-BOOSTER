using System.Collections;
using System.Globalization;

namespace Booster.Platform.Windows.Common;

public sealed class CimRow
{
    private readonly Dictionary<string, object?> _properties;

    public CimRow(IReadOnlyDictionary<string, object?> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        _properties = new(properties, StringComparer.OrdinalIgnoreCase);
    }

    public string? GetString(string name) => ToText(Raw(name));

    public long? GetInt64(string name) => ToInt64(Raw(name));

    public int? GetInt32(string name) => ToInt32(Raw(name));

    public bool? GetBoolean(string name) => Raw(name) switch
    {
        bool b => b,
        string s when bool.TryParse(s.Trim(), out var parsed) => parsed,
        _ => null,
    };

    public IReadOnlyList<string> GetStringArray(string name) => Items(Raw(name)).Select(ToText).OfType<string>().ToArray();

    public IReadOnlyList<int> GetInt32Array(string name) =>
        Items(Raw(name)).Select(ToInt32).Where(v => v.HasValue).Select(v => v!.Value).ToArray();

    private object? Raw(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _properties.GetValueOrDefault(name);
    }

    private static IEnumerable<object?> Items(object? value) => value switch
    {
        null => [],
        string s => [s],
        IEnumerable items => items.Cast<object?>(),
        _ => [value],
    };

    private static string? ToText(object? value)
    {
        var text = value switch
        {
            null or IEnumerable and not string => null,
            string s => s,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
        text = text?.Trim().Trim('\0').Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static int? ToInt32(object? value) =>
        ToInt64(value) is { } v && v is >= int.MinValue and <= int.MaxValue ? (int)v : null;

    private static long? ToInt64(object? value) => value switch
    {
        long l => l,
        int i => i,
        short s => s,
        sbyte sb => sb,
        ulong ul => ul <= long.MaxValue ? (long)ul : null,
        uint ui => ui,
        ushort us => us,
        byte b => b,
        double d => FromIntegralDouble(d),
        float f => FromIntegralDouble(f),
        string s => long.TryParse(s.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null,
        _ => null,
    };

    // 2^63 точно представимо в double; всё, что не меньше, в long не помещается.
    private static long? FromIntegralDouble(double d) =>
        double.IsFinite(d) && Math.Floor(d) == d && d >= long.MinValue && d < 9223372036854775808.0 ? (long)d : null;
}
