using System.Globalization;

namespace Booster.Platform.Windows.Common;

public static class DmtfDate
{
    private const int FullLength = 25;

    private const int MinimumPlausibleYear = 1980;

    private const int MaximumOffsetMinutes = 14 * 60;

    public static DateOnly? ParseDate(string? dmtf)
    {
        if (dmtf is null || dmtf.Length < 8)
            return null;
        if (!DateOnly.TryParseExact(dmtf.AsSpan(0, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;
        return date.Year < MinimumPlausibleYear ? null : date;
    }

    public static DateTimeOffset? ParseDateTime(string? dmtf)
    {
        if (dmtf is null || dmtf.Length != FullLength || dmtf[14] != '.' || dmtf[21] is not ('+' or '-'))
            return null;

        if (!DateTime.TryParseExact(dmtf.AsSpan(0, 14), "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var local) || local.Year < MinimumPlausibleYear)
            return null;

        var fraction = dmtf.AsSpan(15, 6);
        long microseconds = 0;
        if (fraction is not "******" && !long.TryParse(fraction, NumberStyles.None, CultureInfo.InvariantCulture, out microseconds))
            return null;

        if (!int.TryParse(dmtf.AsSpan(22, 3), NumberStyles.None, CultureInfo.InvariantCulture, out var offsetMinutes)
            || offsetMinutes > MaximumOffsetMinutes)
            return null;
        if (dmtf[21] == '-')
            offsetMinutes = -offsetMinutes;

        return new DateTimeOffset(local.AddTicks(microseconds * 10), TimeSpan.FromMinutes(offsetMinutes));
    }
}
