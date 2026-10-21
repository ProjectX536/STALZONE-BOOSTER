using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Providers.Gpu.Resources;

namespace Booster.Providers.Gpu.Nvidia;

/// <param name="Format">Имя значения; null — значение не из списка.</param>
internal sealed record NvDrsSettingEntry(uint SettingId, string DisplayName, Func<uint, string?> Format);

/// <summary>
/// Параметры драйвера NVIDIA, которые Booster показывает (только чтение). Идентификаторы и значения —
/// NvApiDriverSettings.h (NVIDIA/nvapi, R560).
/// </summary>
internal static class NvDrsSettingCatalog
{
    public static IReadOnlyList<NvDrsSettingEntry> Entries { get; } =
    [
        // PREFERRED_PSTATE_ID.
        new(0x1057EB71, ProviderStrings.NvDrsPowerMode, v => v switch
        {
            0 => ProviderStrings.NvDrsPowerAdaptive,
            1 => ProviderStrings.NvDrsPowerMaximum,
            2 => ProviderStrings.NvDrsPowerDriverControlled,
            3 => ProviderStrings.NvDrsPowerConsistent,
            4 => ProviderStrings.NvDrsPowerMinimum,
            5 => ProviderStrings.NvDrsPowerOptimal,
            _ => null,
        }),
        // FRL_FPS_ID: 0 — выключено, иначе предел в кадрах/с.
        new(0x10835002, ProviderStrings.NvDrsFrameLimit, v => v == 0
            ? ProviderStrings.NvDrsOff
            : string.Format(CultureInfo.CurrentCulture, ProviderStrings.NvDrsFpsFormat, v)),
        // PRERENDERLIMIT_ID: 0 — определяет приложение, иначе число кадров.
        new(0x007BA09E, ProviderStrings.NvDrsPrerenderedFrames, v => v == 0
            ? ProviderStrings.NvDrsApplicationControlled
            : v.ToString(CultureInfo.CurrentCulture)),
        // VSYNCMODE_ID.
        new(0x00A879CF, ProviderStrings.NvDrsVSync, v => v switch
        {
            0x60925292 => ProviderStrings.NvDrsApplicationControlled,
            0x08416747 => ProviderStrings.NvDrsOff,
            0x47814940 => ProviderStrings.NvDrsOn,
            0x32610244 => ProviderStrings.NvDrsVSyncHalf,
            0x71271021 => ProviderStrings.NvDrsVSyncThird,
            0x13245256 => ProviderStrings.NvDrsVSyncQuarter,
            0x18888888 => ProviderStrings.NvDrsVSyncFast,
            _ => null,
        }),
        // VRR_MODE_ID.
        new(0x1194F158, ProviderStrings.NvDrsVrrMode, v => v switch
        {
            0 => ProviderStrings.NvDrsVrrOff,
            1 => ProviderStrings.NvDrsVrrFullscreen,
            2 => ProviderStrings.NvDrsVrrFullscreenAndWindowed,
            _ => null,
        }),
        // QUALITY_ENHANCEMENTS_ID.
        new(0x00CE2691, ProviderStrings.NvDrsTextureQuality, v => v switch
        {
            0xFFFFFFF6 => ProviderStrings.NvDrsTextureHighQuality,
            0 => ProviderStrings.NvDrsTextureQualityValue,
            0x0A => ProviderStrings.NvDrsTexturePerformance,
            0x14 => ProviderStrings.NvDrsTextureHighPerformance,
            _ => null,
        }),
    ];

    public static IReadOnlyList<uint> SettingIds { get; } = Entries.Select(e => e.SettingId).ToArray();

    /// <summary>
    /// <paramref name="value"/> null — ключа нет в профиле (SETTING_NOT_FOUND): NotSet, действует значение драйвера.
    /// isCurrentPredefined — DriverDefault, иначе UserChanged.
    /// </summary>
    public static GpuSetting Describe(NvDrsSettingEntry entry, NvDrsValue? value)
    {
        var id = string.Create(CultureInfo.InvariantCulture, $"0x{entry.SettingId:X8}");
        if (value is null)
            return new(id, entry.DisplayName, Reading<string>.Unknown(NvTelemetryMapper.Source, ProviderStrings.NvDrsNotSet),
                GpuSettingOrigin.NotSet, IsReadOnly: true);

        var text = entry.Format(value.Value)
            ?? string.Format(CultureInfo.CurrentCulture, ProviderStrings.NvDrsUnknownValueFormat, value.Value);
        return new(id, entry.DisplayName, Reading<string>.Known(text, NvTelemetryMapper.Source),
            value.IsCurrentPredefined ? GpuSettingOrigin.DriverDefault : GpuSettingOrigin.UserChanged, IsReadOnly: true);
    }
}
