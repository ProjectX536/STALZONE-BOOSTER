using Booster.Core.Resources;

namespace Booster.Core.Reports;

/// <summary>Текст значения перечисления для отчёта и UI: ресурс <c>{Тип}_{Значение}</c> из <see cref="ReportStrings"/>.</summary>
public static class EnumText
{
    /// <summary>Значения без ресурса (в том числе вне перечисления) показываются как есть.</summary>
    public static string Format<TEnum>(TEnum value)
        where TEnum : struct, Enum => Format((Enum)value);

    /// <summary>Для упакованного значения, тип которого известен только во время выполнения (привязки XAML).</summary>
    public static string Format(Enum value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var name = value.ToString();
        return ReportStrings.ResourceManager.GetString($"{value.GetType().Name}_{name}", ReportStrings.Culture) ?? name;
    }
}
