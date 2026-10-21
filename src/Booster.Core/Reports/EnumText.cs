using Booster.Core.Resources;

namespace Booster.Core.Reports;

public static class EnumText
{
    public static string Format<TEnum>(TEnum value)
        where TEnum : struct, Enum => Format((Enum)value);

    public static string Format(Enum value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var name = value.ToString();
        return ReportStrings.ResourceManager.GetString($"{value.GetType().Name}_{name}", ReportStrings.Culture) ?? name;
    }
}
