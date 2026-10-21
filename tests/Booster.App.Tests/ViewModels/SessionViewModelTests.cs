using System.Globalization;
using Booster.App.Resources;
using Booster.App.ViewModels;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Metrics;
using Booster.Core.Profiles;
using Booster.Core.Recovery;
using Booster.Core.Reports;
using Booster.Core.Sensors;
using Booster.Core.Tests.TestSupport;

namespace Booster.App.Tests.ViewModels;

/// <summary>
/// Датчики на панели сессии. Довести координатор до Gaming на фейках без применимых действий нельзя, поэтому
/// проверяется раскладка хода сессии в строки панели — её же вызывает ShowStatus.
/// </summary>
public sealed class SessionViewModelTests
{
    [Fact]
    public void Throttle_finding_shows_cooling_hint()
    {
        var sensors = SensorSamples.Snapshot(0, SensorSamples.Telemetry(reasons: GpuThrottleReasons.Thermal));
        var finding = new ThrottleFinding(ThrottleSignal.GpuDriverThermal, ThrottleConfidence.High, SensorSamples.Gpu.Name, 1, 1,
            Reading<double>.Known(70, SensorSamples.Source));
        var progress = new SessionProgress(SessionState.Gaming, null, null, true, sensors, new ThrottleAssessment(1, 10, false, [finding]));

        var view = SessionViewModel.DescribeSensors(progress, collectCpu: true);

        Assert.True(view.CoolingHint);
        Assert.Equal("Перегрев или снижение частот — проверьте охлаждение и питание", Strings.SessionCoolingHint);
        Assert.Contains(EnumText.Format(ThrottleSignal.GpuDriverThermal), view.ThrottleText, StringComparison.Ordinal);
        Assert.Contains(view.Lines, line => line.StartsWith(SensorSamples.Gpu.Name, StringComparison.Ordinal) && line.Contains("95", StringComparison.Ordinal));
        Assert.Contains(view.Lines, line => line.StartsWith(Core.Resources.ReportStrings.SectionCpu, StringComparison.Ordinal));
    }

    [Fact]
    public void Without_findings_there_is_no_hint_and_cpu_follows_the_profile()
    {
        var sensors = SensorSamples.Snapshot(0, SensorSamples.Telemetry());
        var progress = new SessionProgress(SessionState.Gaming, null, null, true, sensors, new ThrottleAssessment(1, 10, false, []));

        var view = SessionViewModel.DescribeSensors(progress, collectCpu: false);

        Assert.False(view.CoolingHint);
        Assert.Equal(Strings.SessionNoThrottle, view.ThrottleText);
        Assert.DoesNotContain(view.Lines, line => line.StartsWith(Core.Resources.ReportStrings.SectionCpu, StringComparison.Ordinal));
    }

    [Fact]
    public void Without_sensors_nothing_is_shown()
    {
        var view = SessionViewModel.DescribeSensors(new SessionProgress(SessionState.Gaming, null, null, true), collectCpu: true);

        Assert.Empty(view.Lines);
        Assert.Null(view.ThrottleText);
        Assert.False(view.CoolingHint);
    }

    private static readonly TimeSpan Planned = TimeSpan.FromSeconds(300);

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private static string Number(double value) => value.ToString("0.0", CultureInfo.CurrentCulture);

    private static SessionFrameSummary Completed(double durationMs) => new(SessionFrameStatus.Completed, Planned,
        new FrameMetricsResult(FrameMetricsStatus.Ok, new FrameMetrics(18_000, durationMs, 143.24, 98.06, 7, 9, 12), 18_000, 0),
        new StutterResult(2, 0.44, 0.1, 40, 0.5), null);

    [Fact]
    public void Completed_frames_show_average_low_and_stutters_for_the_first_minutes()
    {
        var text = SessionViewModel.DescribeFrames(Completed(299_600));

        Assert.Equal(Format(Strings.SessionFramesFormat, Format(Strings.SessionFramesMinutesFormat, 5), Number(143.24), Number(98.06),
            Number(0.44)), text);
    }

    [Theory]
    [InlineData(130_000, 2, true)]
    [InlineData(45_000, 45, false)]
    public void Capture_ended_early_shows_its_actual_length(double durationMs, int expected, bool minutes)
    {
        var text = SessionViewModel.DescribeFrames(Completed(durationMs));

        var length = Format(minutes ? Strings.SessionFramesMinutesFormat : Strings.SessionFramesSecondsFormat, expected);
        Assert.Equal(Format(Strings.SessionFramesFormat, length, Number(143.24), Number(98.06), Number(0.44)), text);
    }

    [Fact]
    public void Too_few_frames_are_explained_with_their_count()
    {
        var frames = new SessionFrameSummary(SessionFrameStatus.Completed, Planned,
            new FrameMetricsResult(FrameMetricsStatus.InsufficientData, null, 12, 0), null, null);

        Assert.Equal(Format(Strings.SessionFramesTooFewFormat, 12), SessionViewModel.DescribeFrames(frames));
    }

    [Fact]
    public void Capture_in_progress_names_its_planned_length()
    {
        var frames = new SessionFrameSummary(SessionFrameStatus.Capturing, Planned, null, null, null);

        Assert.Equal(Format(Strings.SessionFramesCapturingFormat, Format(Strings.SessionFramesMinutesFormat, 5)),
            SessionViewModel.DescribeFrames(frames));
    }

    [Theory]
    [InlineData(SessionFrameStatus.NeedsElevation, nameof(Strings.SessionFramesNeedsElevation))]
    [InlineData(SessionFrameStatus.NotInstalled, nameof(Strings.SessionFramesNotInstalled))]
    [InlineData(SessionFrameStatus.Failed, nameof(Strings.SessionFramesFailed))]
    public void Capture_that_did_not_run_says_why(SessionFrameStatus status, string key)
    {
        var frames = new SessionFrameSummary(status, Planned, null, null, "detail");

        Assert.Equal(Strings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture), SessionViewModel.DescribeFrames(frames));
    }

    [Fact]
    public void Disabled_or_not_yet_known_capture_shows_nothing()
    {
        Assert.Null(SessionViewModel.DescribeFrames(null));
        Assert.Null(SessionViewModel.DescribeFrames(new SessionFrameSummary(SessionFrameStatus.Disabled, Planned, null, null, null)));
    }
}
