using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Core.Reports;
using Booster.Core.SystemHealth;

namespace Booster.Core.Tests.SystemHealth;

public class GamingAdvisorTests
{
    private const string Source = "test";

    private static Reading<GamingSettings> Gaming(Reading<bool> gameMode, Reading<bool> recording) =>
        Reading<GamingSettings>.Known(new GamingSettings(gameMode, recording), Source);

    private static Reading<IReadOnlyList<RunningOverlay>> Overlays(params RunningOverlay[] overlays) =>
        Reading<IReadOnlyList<RunningOverlay>>.Known(overlays, Source);

    [Fact]
    public void Unknown_values_give_nothing()
    {
        Assert.Empty(GamingAdvisor.Advise(Reading<GamingSettings>.Failure(Source, "denied"),
            Reading<IReadOnlyList<RunningOverlay>>.Unknown(Source)));
        Assert.Empty(GamingAdvisor.Advise(Gaming(Reading<bool>.Unknown(Source), Reading<bool>.Unknown(Source)), Overlays()));
    }

    [Fact]
    public void Background_recording_gives_info()
    {
        var advice = Assert.Single(GamingAdvisor.Advise(Gaming(Reading<bool>.Known(false, Source), Reading<bool>.Known(true, Source)),
            Overlays()));

        Assert.Equal((AdviceId.BackgroundRecordingOn, AdviceModule.Startup, FindingSeverity.Info, SafetyLevel.L3ManualOnly),
            (advice.Id, advice.Module, advice.Severity, advice.Level));
        Assert.Empty(advice.Arguments);
        Assert.Empty(GamingAdvisor.Advise(Gaming(Reading<bool>.Known(false, Source), Reading<bool>.Known(false, Source)), Overlays()));
    }

    [Fact]
    public void Running_overlays_are_listed()
    {
        var advice = Assert.Single(GamingAdvisor.Advise(Reading<GamingSettings>.Unknown(Source),
            Overlays(new RunningOverlay("RTSS", "RivaTuner Statistics Server", [10]), new RunningOverlay("Discord", "Discord", [11, 12]))));

        Assert.Equal((AdviceId.OverlaysRunning, AdviceModule.Startup, FindingSeverity.Info, SafetyLevel.L3ManualOnly),
            (advice.Id, advice.Module, advice.Severity, advice.Level));
        Assert.Equal(["RivaTuner Statistics Server, Discord"], advice.Arguments);
    }

    [Fact]
    public void Game_mode_is_not_judged_here()
    {
        Assert.Empty(GamingAdvisor.Advise(Gaming(Reading<bool>.Known(false, Source), Reading<bool>.Unknown(Source)), Overlays()));
    }
}
