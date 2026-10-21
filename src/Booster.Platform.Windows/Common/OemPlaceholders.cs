namespace Booster.Platform.Windows.Common;

public static class OemPlaceholders
{
    private static readonly HashSet<string> Placeholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "To Be Filled By O.E.M.",
        "Default string",
        "System Product Name",
        "System manufacturer",
        "System Version",
        "Not Applicable",
        "Not Specified",
        "O.E.M.",
        "OEM",
        "None",
        "Unknown",
    };

    public static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || Placeholders.Contains(trimmed) ? null : trimmed;
    }
}
