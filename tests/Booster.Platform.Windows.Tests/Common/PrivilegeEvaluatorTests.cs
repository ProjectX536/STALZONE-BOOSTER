using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Tests.Common;

public class PrivilegeEvaluatorTests
{
    private const string PerformanceLogUsers = "S-1-5-32-559";

    [Fact]
    public void Ordinary_user_cannot_capture_frames_without_prompt()
    {
        var state = PrivilegeEvaluator.Evaluate(false, []);

        Assert.False(state.IsElevated);
        Assert.False(state.IsPerformanceLogUser);
        Assert.False(state.CanCaptureFramesWithoutPrompt);
    }

    [Fact]
    public void Performance_log_user_can_capture_frames_without_elevation()
    {
        var state = PrivilegeEvaluator.Evaluate(false, ["S-1-5-32-545", PerformanceLogUsers]);

        Assert.False(state.IsElevated);
        Assert.True(state.IsPerformanceLogUser);
        Assert.True(state.CanCaptureFramesWithoutPrompt);
    }

    [Fact]
    public void Elevated_process_can_capture_frames()
    {
        var state = PrivilegeEvaluator.Evaluate(true, []);

        Assert.True(state.IsElevated);
        Assert.True(state.CanCaptureFramesWithoutPrompt);
    }

    [Fact]
    public void Similar_sid_is_not_mistaken_for_performance_log_users()
    {
        Assert.False(PrivilegeEvaluator.Evaluate(false, ["S-1-5-32-5590", "S-1-5-32-55"]).IsPerformanceLogUser);
    }
}
