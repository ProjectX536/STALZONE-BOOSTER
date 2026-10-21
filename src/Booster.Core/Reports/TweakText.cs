using System.Globalization;
using Booster.Core.Configuration;
using Booster.Core.Resources;
using Booster.Core.SystemHealth;

namespace Booster.Core.Reports;

/// <summary>
/// Тексты настройки белого списка реестра — общие для действия, отчёта и экрана «Система»: название
/// <c>Action_RegistryTweak_*</c>, основание <c>TweakEvidence_*</c>.
/// </summary>
public static class TweakText
{
    /// <returns>null — Id не из белого списка.</returns>
    public static string? Name(string id) => id switch
    {
        RegistryTweakIds.GameMode => ReportStrings.Action_RegistryTweak_GameMode,
        RegistryTweakIds.GpuHighPerformance => ReportStrings.Action_RegistryTweak_GpuHighPerformance,
        RegistryTweakIds.WindowedFlipModel => ReportStrings.Action_RegistryTweak_WindowedFlipModel,
        _ => null,
    };

    /// <summary>Что показали снимок или серия бенчмарка; без доказательства — что признака нет.</summary>
    public static string Evidence(TweakProposal proposal, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(culture);

        var format = proposal.Definition.Id switch
        {
            RegistryTweakIds.GameMode => ReportStrings.TweakEvidence_GameMode,
            RegistryTweakIds.GpuHighPerformance => ReportStrings.TweakEvidence_GpuHighPerformance,
            RegistryTweakIds.WindowedFlipModel => ReportStrings.TweakEvidence_WindowedFlipModel,
            _ => null,
        };
        return proposal.State == TweakState.NotNeeded || format is null
            ? ReportStrings.TweakNoEvidence
            : string.Format(culture, format, [.. proposal.Evidence]);
    }
}
