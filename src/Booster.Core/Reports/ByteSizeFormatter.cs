using System.Globalization;
using Booster.Core.Resources;

namespace Booster.Core.Reports;

/// <summary>Размер с основанием 1024: «8 ГБ», «15,8 ГБ». Один знак после запятой, у целого — без дробной части.</summary>
public static class ByteSizeFormatter
{
    private const double Base = 1024;

    public static string Format(long bytes, CultureInfo culture)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        ArgumentNullException.ThrowIfNull(culture);

        string[] units = [ReportStrings.ByteUnitB, ReportStrings.ByteUnitKB, ReportStrings.ByteUnitMB, ReportStrings.ByteUnitGB,
            ReportStrings.ByteUnitTB];
        if (bytes < Base)
            return $"{bytes.ToString(culture)} {units[0]}";

        double value = bytes;
        var unit = 0;
        while (value >= Base && unit < units.Length - 1)
        {
            value /= Base;
            unit++;
        }

        var rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        // 1023,96 КБ округляется до 1024,0 — это уже 1 МБ.
        if (rounded >= Base && unit < units.Length - 1)
        {
            rounded = Math.Round(rounded / Base, 1, MidpointRounding.AwayFromZero);
            unit++;
        }

        var number = rounded.ToString(rounded == Math.Floor(rounded) ? "0" : "0.0", culture);
        return $"{number} {units[unit]}";
    }
}
