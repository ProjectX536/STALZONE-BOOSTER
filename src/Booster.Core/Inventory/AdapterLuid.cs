using System.Globalization;

namespace Booster.Core.Inventory;

public readonly record struct AdapterLuid(uint LowPart, int HighPart)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"0x{HighPart:X8}_0x{LowPart:X8}");

    public static bool TryParse(string? text, out AdapterLuid luid)
    {
        luid = default;
        if (string.IsNullOrEmpty(text))
            return false;

        var parts = text.Split('_');
        if (parts.Length != 2
            || !TryParseHex(parts[0], out var high)
            || !TryParseHex(parts[1], out var low))
            return false;

        luid = new AdapterLuid(low, unchecked((int)high));
        return true;
    }

    private static bool TryParseHex(string part, out uint value)
    {
        value = 0;
        return part.Length is > 2 and <= 10
            && part.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(part.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }
}
