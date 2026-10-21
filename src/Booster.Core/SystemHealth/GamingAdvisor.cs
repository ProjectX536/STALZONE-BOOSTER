using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Core.Reports;

namespace Booster.Core.SystemHealth;

public static class GamingAdvisor
{
    private const string NameSeparator = ", ";

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
