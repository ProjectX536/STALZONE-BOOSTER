using System.Globalization;
using Booster.Core.Resources;
using Booster.Core.SystemHealth;

namespace Booster.Core.Tests.Reports;

public class AdviceTextTests
{
    private static readonly Dictionary<AdviceId, int> ArgumentCount = new()
    {
        [AdviceId.PagefileDisabled] = 0,
        [AdviceId.PagefileManual] = 1,
        [AdviceId.CommitNearLimit] = 1,
        [AdviceId.CommitPeakNearLimit] = 1,
        [AdviceId.PagefileVolumeLowSpace] = 1,
        [AdviceId.PagefileOnHdd] = 1,
        [AdviceId.MemoryBelowRatedSpeed] = 2,
        [AdviceId.SingleMemoryModule] = 0,
        [AdviceId.MixedMemoryModules] = 0,
        [AdviceId.ThirdPartyServicesAutostart] = 1,
        [AdviceId.UnexpectedShutdowns] = 1,
        [AdviceId.BugChecks] = 2,
        [AdviceId.DisplayDriverResets] = 1,
        [AdviceId.HardwareErrors] = 1,
        [AdviceId.DiskErrors] = 1,
        [AdviceId.GameCrashes] = 2,
        [AdviceId.NetworkHighLatency] = 2,
        [AdviceId.NetworkJitter] = 2,
        [AdviceId.NetworkLoss] = 2,
        [AdviceId.NetworkWireless] = 0,
        [AdviceId.HeavyStartupItems] = 1,
        [AdviceId.HeavyMachineStartupItems] = 1,
        [AdviceId.OverlaysRunning] = 1,
        [AdviceId.BackgroundRecordingOn] = 0,
        [AdviceId.GameModeOff] = 0,
        [AdviceId.GpuOnIntegrated] = 2,
        [AdviceId.LegacyWindowedPresent] = 2,
        [AdviceId.DpcTimeHigh] = 1,
        [AdviceId.InterruptTimeHigh] = 1,
    };

    private static string Text(string key)
    {
        var text = ReportStrings.ResourceManager.GetString(key, ReportStrings.Culture);
        Assert.False(string.IsNullOrWhiteSpace(text), $"ReportStrings has no {key}");
        return text!;
    }

    [Fact]
    public void Every_advice_has_title_text_action_and_risk()
    {
        foreach (var id in Enum.GetValues<AdviceId>())
        {
            Text($"AdviceId_{id}");
            Text($"AdviceText_{id}");
            Text($"AdviceAction_{id}");
            Text($"AdviceRisk_{id}");
        }

        foreach (var module in Enum.GetValues<AdviceModule>())
            Text($"AdviceModule_{module}");
        foreach (var mode in Enum.GetValues<PagefileMode>())
            Text($"PagefileMode_{mode}");
    }

    [Fact]
    public void Advice_text_uses_exactly_the_arguments_of_its_rule()
    {
        Assert.Equal(Enum.GetValues<AdviceId>().Order(), ArgumentCount.Keys.Order());

        foreach (var (id, count) in ArgumentCount)
        {
            var text = Text($"AdviceText_{id}");
            var arguments = Enumerable.Range(0, count).Select(i => $"<{i}>").ToArray<object?>();

            var formatted = string.Format(CultureInfo.InvariantCulture, text, arguments);

            for (var i = 0; i < count; i++)
                Assert.True(formatted.Contains($"<{i}>", StringComparison.Ordinal), $"AdviceText_{id} does not use {{{i}}}");
            Assert.DoesNotContain($"{{{count}}}", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Network_advice_text_warns_that_fps_is_not_promised()
    {
        foreach (var id in new[] { AdviceId.NetworkHighLatency, AdviceId.NetworkJitter, AdviceId.NetworkLoss, AdviceId.NetworkWireless })
            Assert.Contains("FPS", Text($"AdviceText_{id}"), StringComparison.Ordinal);
    }
}
