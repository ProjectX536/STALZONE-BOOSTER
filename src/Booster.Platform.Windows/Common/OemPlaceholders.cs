namespace Booster.Platform.Windows.Common;

/// <summary>
/// Строки-заглушки, которые производители оставляют в SMBIOS вместо реальных значений.
/// Показывать их как модель или производителя нельзя: это не данные, а их отсутствие.
/// </summary>
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
        // Так модули памяти и платы без заполненного SPD/SMBIOS сообщают производителя.
        "Unknown",
    };

    /// <summary>Обрезанное значение; заглушка или пустая строка → null.</summary>
    public static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || Placeholders.Contains(trimmed) ? null : trimmed;
    }
}
