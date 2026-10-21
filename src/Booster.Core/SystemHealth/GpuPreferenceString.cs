namespace Booster.Core.SystemHealth;

public static class GpuPreferenceString
{
    private const char PairSeparator = ';';
    private const char KeySeparator = '=';

    public static string Set(string? current, string pair)
    {
        var key = Key(pair) ?? throw new ArgumentException($"'{pair}' is not a Key=Value pair", nameof(pair));
        var pairs = new List<string>();
        var replaced = false;
        foreach (var existing in Split(current))
        {
            if (!string.Equals(Key(existing), key, StringComparison.OrdinalIgnoreCase))
                pairs.Add(existing);
            else if (!replaced)
            {
                pairs.Add(pair.Trim());
                replaced = true;
            }
        }

        if (!replaced)
            pairs.Add(pair.Trim());
        return string.Concat(pairs.Select(p => p + PairSeparator));
    }

    public static bool Contains(string? current, string pair)
    {
        var key = Key(pair);
        var value = Value(pair);
        return key is not null && Split(current).Any(p =>
            string.Equals(Key(p), key, StringComparison.OrdinalIgnoreCase) && string.Equals(Value(p), value, StringComparison.Ordinal));
    }

    private static IEnumerable<string> Split(string? current) =>
        (current ?? "").Split(PairSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? Key(string pair)
    {
        var index = pair.IndexOf(KeySeparator, StringComparison.Ordinal);
        var key = index > 0 ? pair[..index].Trim() : "";
        return key.Length > 0 ? key : null;
    }

    private static string? Value(string pair)
    {
        var index = pair.IndexOf(KeySeparator, StringComparison.Ordinal);
        return index > 0 ? pair[(index + 1)..].Trim() : null;
    }
}
