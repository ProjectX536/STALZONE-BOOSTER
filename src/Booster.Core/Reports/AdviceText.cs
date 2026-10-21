using System.Globalization;
using Booster.Core.Resources;
using Booster.Core.SystemHealth;

namespace Booster.Core.Reports;

public static class AdviceText
{
    public static string Title(SystemAdvice advice)
    {
        ArgumentNullException.ThrowIfNull(advice);
        return EnumText.Format(advice.Id);
    }

    public static string Body(SystemAdvice advice, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(advice);
        ArgumentNullException.ThrowIfNull(culture);
        return string.Format(culture, Resource("AdviceText", advice.Id), [.. advice.Arguments]);
    }

    public static string Summary(SystemAdvice advice, CultureInfo culture) =>
        string.Format(culture, ReportStrings.AdviceCellFormat, Title(advice), Body(advice, culture));

    public static string Action(SystemAdvice advice)
    {
        ArgumentNullException.ThrowIfNull(advice);
        return Resource("AdviceAction", advice.Id);
    }

    public static string Risk(SystemAdvice advice)
    {
        ArgumentNullException.ThrowIfNull(advice);
        return Resource("AdviceRisk", advice.Id);
    }

    private static string Resource(string prefix, AdviceId id) =>
        ReportStrings.ResourceManager.GetString($"{prefix}_{id}", ReportStrings.Culture) ?? id.ToString();
}
