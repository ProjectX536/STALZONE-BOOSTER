using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Core.Reports;

namespace Booster.Core.SystemHealth;

/// <summary>
/// Фоновая запись и программы с оверлеем — только рекомендации L3: Booster не меняет настройки GameDVR и не закрывает программы.
/// </summary>
public static class GamingAdvisor
{
    private const string NameSeparator = ", ";

    /// <summary>
    /// Модуль Startup. BackgroundRecording true → BackgroundRecordingOn (Info, L3, ms-settings:gaming-gamedvr);
    /// оверлеи → OverlaysRunning (Info, L3, имена через «, »). Unknown рекомендаций не даёт.
    /// Game Mode здесь не оценивается: рекомендацию GameModeOff даёт TweakAdvisor, уровень зависит от сборки.
    /// </summary>
    public static IReadOnlyList<SystemAdvice> Advise(Reading<GamingSettings> gaming, Reading<IReadOnlyList<RunningOverlay>> overlays)
    {
        var advice = new List<SystemAdvice>();
        if (gaming is { IsAvailable: true, Value.BackgroundRecording: { IsAvailable: true, Value: true } })
            advice.Add(new SystemAdvice(AdviceId.BackgroundRecordingOn, AdviceModule.Startup, FindingSeverity.Info, SafetyLevel.L3ManualOnly, []));
        if (overlays is { IsAvailable: true, Value: { Count: > 0 } running })
        {
            advice.Add(new SystemAdvice(AdviceId.OverlaysRunning, AdviceModule.Startup, FindingSeverity.Info, SafetyLevel.L3ManualOnly,
                [string.Join(NameSeparator, running.Select(o => o.Name))]));
        }

        return advice;
    }
}
