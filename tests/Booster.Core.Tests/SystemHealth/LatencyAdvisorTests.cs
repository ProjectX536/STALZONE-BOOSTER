using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.Reports;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.SystemHealth;

public class LatencyAdvisorTests
{
    private static readonly BottleneckSettings Settings = FakeConfiguration.DefaultThresholds.Bottleneck;

    private static Reading<double> Percent(double value) => Reading<double>.Known(value, SampleSeries.Source);

    private static SeriesTelemetry Telemetry(Reading<double> dpcPeak, Reading<double> interruptPeak) =>
        SampleSeries.Full().Telemetry with { DpcTimePeakPercent = dpcPeak, InterruptTimePeakPercent = interruptPeak };

    private static void AssertWarning(SystemAdvice advice, AdviceId id, string peak)
    {
        Assert.Equal(id, advice.Id);
        Assert.Equal(AdviceModule.Latency, advice.Module);
        Assert.Equal(FindingSeverity.Warning, advice.Severity);
        Assert.Equal(SafetyLevel.L3ManualOnly, advice.Level);
        Assert.Equal([peak], advice.Arguments);
    }

    [Fact]
    public void Unknown_or_no_series_gives_nothing()
    {
        Assert.Empty(LatencyAdvisor.Advise(null, Settings));
        Assert.Empty(LatencyAdvisor.Advise(Telemetry(Reading<double>.Unknown(), Reading<double>.NeedsAdmin(SampleSeries.Source)), Settings));
        Assert.Empty(LatencyAdvisor.Advise(Telemetry(Percent(Settings.DpcTimePercent - 0.1), Percent(Settings.InterruptTimePercent - 0.1)),
            Settings));
    }

    [Fact]
    public void High_dpc_peak_gives_warning()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
        try
        {
            var advice = Assert.Single(LatencyAdvisor.Advise(Telemetry(Percent(2.4), Reading<double>.Unknown()), Settings));

            AssertWarning(advice, AdviceId.DpcTimeHigh, "2.4");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void High_interrupt_peak_gives_warning()
    {
        var advice = Assert.Single(LatencyAdvisor.Advise(Telemetry(Percent(1), Percent(Settings.InterruptTimePercent)), Settings));

        AssertWarning(advice, AdviceId.InterruptTimeHigh, "2");

        var both = LatencyAdvisor.Advise(Telemetry(Percent(5), Percent(3.26)), Settings);
        Assert.Equal([AdviceId.DpcTimeHigh, AdviceId.InterruptTimeHigh], both.Select(a => a.Id));
        Assert.Equal(["5", "3.3"], both.Select(a => a.Arguments[0]));
    }
}
