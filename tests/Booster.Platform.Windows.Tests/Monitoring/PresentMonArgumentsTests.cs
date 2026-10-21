using Booster.Core.Abstractions;
using Booster.Platform.Windows.Monitoring;

namespace Booster.Platform.Windows.Tests.Monitoring;

public class PresentMonArgumentsTests
{
    private const string Csv = @"C:\Users\u\AppData\Local\Booster\captures\20261017-143005-My Game.exe.csv";

    [Fact]
    public void Capture_by_name_keeps_the_name_as_one_argument()
    {
        var args = PresentMonArguments.ForCapture(new FrameCaptureRequest("My Game.exe", null, TimeSpan.FromSeconds(60)), Csv, "BoosterCapture");

        Assert.Equal(
        [
            "--process_name", "My Game.exe", "--output_file", Csv, "--qpc_time", "--timed", "60", "--terminate_after_timed",
            "--no_console_stats", "--session_name", "BoosterCapture", "--stop_existing_session",
        ], args);
    }

    [Fact]
    public void ForCapture_requests_qpc_time() =>
        Assert.Contains("--qpc_time", PresentMonArguments.ForCapture(new FrameCaptureRequest(null, 1234, TimeSpan.FromSeconds(30)), Csv, "BoosterCapture"));

    [Fact]
    public void Capture_by_process_id_uses_process_id()
    {
        var args = PresentMonArguments.ForCapture(new FrameCaptureRequest(null, 1234, TimeSpan.FromSeconds(60)), Csv, "BoosterCapture");

        Assert.Equal(["--process_id", "1234"], args.Take(2));
        Assert.DoesNotContain("--process_name", args);
    }

    [Theory]
    [InlineData(60, "60")]
    [InlineData(2.5, "3")]
    [InlineData(1, "1")]
    public void Duration_is_rounded_up_to_whole_seconds(double seconds, string expected)
    {
        var args = PresentMonArguments.ForCapture(new FrameCaptureRequest("a.exe", null, TimeSpan.FromSeconds(seconds)), Csv, "S");

        Assert.Equal(expected, args[args.ToList().IndexOf("--timed") + 1]);
    }

    [Fact]
    public void Non_positive_duration_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PresentMonArguments.ForCapture(new FrameCaptureRequest("a.exe", null, TimeSpan.Zero), Csv, "S"));

    [Theory]
    [InlineData("a.exe", 1)]
    [InlineData(null, null)]
    [InlineData(" ", null)]
    [InlineData(null, 0)]
    public void Exactly_one_valid_target_is_required(string? name, int? processId) =>
        Assert.ThrowsAny<ArgumentException>(() =>
            PresentMonArguments.ForCapture(new FrameCaptureRequest(name, processId, TimeSpan.FromSeconds(10)), Csv, "S"));

    [Fact]
    public void ForCapture_with_stop_on_exit_adds_terminate_on_proc_exit()
    {
        var request = new FrameCaptureRequest(null, 1234, TimeSpan.FromSeconds(300)) { StopWhenProcessExits = true };

        Assert.Contains("--terminate_on_proc_exit", PresentMonArguments.ForCapture(request, Csv, "BoosterCapture"));
        Assert.DoesNotContain("--terminate_on_proc_exit",
            PresentMonArguments.ForCapture(request with { StopWhenProcessExits = false }, Csv, "BoosterCapture"));
    }

    [Fact]
    public void Game_session_capture_has_its_own_etw_session_name()
    {
        Assert.Equal("BoosterCapture", PresentMonArguments.SessionName("BoosterCapture", FrameCapturePurpose.Benchmark));
        Assert.NotEqual("BoosterCapture", PresentMonArguments.SessionName("BoosterCapture", FrameCapturePurpose.GameSession));
        Assert.StartsWith("BoosterCapture", PresentMonArguments.SessionName("BoosterCapture", FrameCapturePurpose.GameSession), StringComparison.Ordinal);
    }

    [Fact]
    public void Terminate_names_the_session() =>
        Assert.Equal(["--terminate_existing_session", "--session_name", "BoosterCapture"], PresentMonArguments.ForTerminate("BoosterCapture"));
}
