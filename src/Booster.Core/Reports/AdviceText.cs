using System.Globalization;
using Booster.Core.Resources;
using Booster.Core.SystemHealth;

namespace Booster.Core.Reports;

/// <summary>
/// Тексты рекомендации из ресурсов <c>AdviceId_*</c>, <c>AdviceText_*</c>, <c>AdviceAction_*</c>, <c>AdviceRisk_*</c> — общие для
/// отчёта и экрана «Система».
/// </summary>
public static class AdviceText
{
    public static string Title(SystemAdvice advice)
    {
        ArgumentNullException.ThrowIfNull(advice);
        return EnumText.Format(advice.Id);
    }

    /// <summary>Текст с подставленными аргументами.</summary>
    public static string Body(SystemAdvice advice, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(advice);
        ArgumentNullException.ThrowIfNull(culture);
        return string.Format(culture, Resource("AdviceText", advice.Id), [.. advice.Arguments]);
    }

    /// <summary>«Название: текст» — одной строкой, для таблицы.</summary>
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
