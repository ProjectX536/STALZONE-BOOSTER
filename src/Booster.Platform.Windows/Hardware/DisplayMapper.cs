using Booster.Core.Abstractions;
using Booster.Core.Inventory;

namespace Booster.Platform.Windows.Hardware;

/// <summary>Активные пути вывода → дисплеи. Поле, которого Windows не сообщила, становится Unknown, а не нулём.</summary>
public static class DisplayMapper
{
    public const string Source = "QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)";

    private const string NameSource = "DISPLAYCONFIG_TARGET_DEVICE_NAME.monitorFriendlyDeviceName";
    private const string ModeSource = "DISPLAYCONFIG_SOURCE_MODE";
    private const string RefreshSource = "DISPLAYCONFIG_PATH_TARGET_INFO.refreshRate";
    private const string ColorSource = "DisplayConfigGetDeviceInfo(GET_ADVANCED_COLOR_INFO)";
    private const string AdapterSource = "DISPLAYCONFIG_PATH_TARGET_INFO.adapterId";

    public static Reading<IReadOnlyList<DisplayInfo>> Map(IReadOnlyList<DisplayPathRecord> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        // RDP-сессия, выключенные или спящие мониторы: активных путей нет. Это не сбой, а отсутствие данных.
        if (paths.Count == 0)
            return Reading<IReadOnlyList<DisplayInfo>>.Unknown(Source, "no active display paths (RDP session or displays off)");
        return Reading<IReadOnlyList<DisplayInfo>>.Known(paths.Select(MapPath).ToArray(), Source);
    }

    private static DisplayInfo MapPath(DisplayPathRecord path) => new(
        string.IsNullOrWhiteSpace(path.MonitorFriendlyName)
            ? Reading<string>.Unknown(NameSource, "monitor did not report a name (no EDID)")
            : Reading<string>.Known(path.MonitorFriendlyName.Trim(), NameSource),
        Dimension(path.Width),
        Dimension(path.Height),
        Refresh(path.RefreshNumerator, path.RefreshDenominator),
        Flag(path.AdvancedColorSupported),
        Flag(path.AdvancedColorEnabled),
        Reading<bool>.Unknown(note: "Windows does not report whether variable refresh rate is active"),
        Reading<AdapterLuid>.Known(path.AdapterLuid, AdapterSource),
        path.IsPrimary);

    private static Reading<int> Dimension(uint pixels) =>
        pixels is > 0 and <= int.MaxValue ? Reading<int>.Known((int)pixels, ModeSource) : Reading<int>.Unknown(ModeSource);

    // Частота приходит дробью: 59940/1000 и 60000/1001 — это 59,94 Гц. Трёх знаков хватает, чтобы отличить 143,856 от 144.
    private static Reading<double> Refresh(uint numerator, uint denominator) =>
        numerator > 0 && denominator > 0
            ? Reading<double>.Known(Math.Round((double)numerator / denominator, 3), RefreshSource)
            : Reading<double>.Unknown(RefreshSource, $"refresh rate {numerator}/{denominator}");

    private static Reading<bool> Flag(bool? value) =>
        value is { } known ? Reading<bool>.Known(known, ColorSource) : Reading<bool>.Unknown(ColorSource, "advanced color info unavailable");
}
