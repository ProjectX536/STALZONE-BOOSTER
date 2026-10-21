using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Platform.Windows.Monitoring;

namespace Booster.Platform.Windows.Tests.Monitoring;

public class PresentMonCsvParserTests
{
    // Реальный захват PresentMon 2.6.0 (firefox.exe, 400 кадров).
    private const string RealCapture = "pm26-firefox.csv";

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "PresentMon", name);

    private static PresentMonCsvParse ParseFile(string name, string? processName, int? processId = null)
    {
        using var reader = new StreamReader(FixturePath(name));
        return PresentMonCsvParser.Parse(reader, processName, processId, FixturePath(name));
    }

    private static PresentMonCsvParse ParseText(string csv, string? processName = "game.exe", int? processId = null) =>
        PresentMonCsvParser.Parse(new StringReader(csv), processName, processId, "inline.csv");

    [Fact]
    public void Real_presentmon_2_6_capture_uses_ms_between_presents()
    {
        var parse = ParseFile(RealCapture, "firefox.exe");

        Assert.Equal(FrameCaptureStatus.Completed, parse.Status);
        var capture = Assert.IsType<FrameCapture>(parse.Capture);
        Assert.Equal("MsBetweenPresents", capture.IntervalColumn);
        Assert.Equal("firefox.exe", capture.ProcessName);
        Assert.Equal(10348, capture.ProcessId);
        Assert.Equal(400, capture.Frames.Count);
        Assert.All(capture.IntervalsMs(), ms => Assert.True(ms > 0));
        Assert.Equal(41.7067, capture.IntervalsMs()[0], 10);
        Assert.Equal(0, capture.DiscardedRows);
        Assert.Empty(capture.FrameTypes);
        Assert.Equal(FixturePath(RealCapture), capture.CsvPath);
    }

    [Fact]
    public void V1_metrics_with_lowercase_column_name_are_read()
    {
        var capture = ParseFile("pm-v1.synthetic.csv", "game.exe").Capture!;

        Assert.Equal("MsBetweenPresents", capture.IntervalColumn);
        Assert.Equal([16.5, 16.8, 17.0, 16.4, 33.3, 16.6, 16.7, 16.9, 16.5, 16.6], capture.IntervalsMs());
    }

    [Fact]
    public void V2_metrics_use_frame_time_and_report_frame_types()
    {
        var parse = ParseFile("pm-v2-frametime.synthetic.csv", "game.exe");

        Assert.Equal(FrameCaptureStatus.Completed, parse.Status);
        Assert.Equal("FrameTime", parse.Capture!.IntervalColumn);
        Assert.Equal(8, parse.Capture.Frames.Count);
        Assert.Equal(["Application", "Intel XeSS-FG"], parse.Capture.FrameTypes);
    }

    [Fact]
    public void Qpc_fixture_gives_frames_with_timestamps_and_busy_times()
    {
        var capture = ParseFile("pm26-qpc.csv", "RobloxPlayerBeta.exe").Capture!;

        Assert.Equal(400, capture.Frames.Count);
        Assert.All(capture.Frames, frame =>
        {
            Assert.NotNull(frame.PresentQpc);
            Assert.NotNull(frame.CpuBusyMs);
            Assert.NotNull(frame.GpuBusyMs);
            Assert.Equal(0, frame.SyncInterval);
        });
        Assert.All(capture.Frames.Zip(capture.Frames.Skip(1)), pair => Assert.True(pair.Second.PresentQpc > pair.First.PresentQpc));
        Assert.Equal(new FrameSample(52702545723, 8.9277, 8.8497, 3.7114, 0), capture.Frames[0]);
        Assert.Equal(["Composed: Flip", "Hardware Composed: Independent Flip", "Hardware: Independent Flip"], capture.PresentModes);
    }

    [Fact]
    public void Default_fixture_without_qpc_has_null_timestamps()
    {
        var capture = ParseFile(RealCapture, "firefox.exe").Capture!;

        Assert.All(capture.Frames, frame => Assert.Null(frame.PresentQpc));
        Assert.Equal(new FrameSample(null, 41.7067, 41.4884, 1.2143, 1), capture.Frames[0]);
        Assert.Equal(["Hardware Composed: Independent Flip"], capture.PresentModes);
    }

    [Fact]
    public void V2_fixture_reads_CPUBusy_and_GPUBusy()
    {
        var capture = ParseFile("pm-v2-frametime.synthetic.csv", "game.exe").Capture!;

        Assert.Equal(new FrameSample(null, 8.3, 7.1, 6.6, 0), capture.Frames[0]);
    }

    [Fact]
    public void V1_fixture_reads_msGPUActive_and_has_no_cpu_busy()
    {
        var capture = ParseFile("pm-v1.synthetic.csv", "game.exe").Capture!;

        Assert.Equal(new FrameSample(null, 16.5, null, 4.2, 0), capture.Frames[0]);
        Assert.All(capture.Frames, frame => Assert.Null(frame.CpuBusyMs));
        Assert.Equal(["Hardware: Independent Flip"], capture.PresentModes);
    }

    [Fact]
    public void NA_busy_value_gives_null_but_keeps_frame()
    {
        const string csv = """
            Application,ProcessID,SwapChainAddress,SyncInterval,PresentMode,TimeInQPC,MsBetweenPresents,MsCPUBusy,MsGPUBusy
            game.exe,1,0xA,NA,,100,16.6,NA,
            game.exe,1,0xA,1,Composed: Flip,NA,16.7,15.5,abc
            game.exe,1,0xA,abc,Composed: Flip,-5,16.8,-2.5,0
            """;

        var capture = ParseText(csv).Capture!;

        Assert.Equal(
        [
            new FrameSample(100, 16.6, null, null, null),
            new FrameSample(null, 16.7, 15.5, null, 1),
            new FrameSample(null, 16.8, null, 0, null),
        ], capture.Frames);
        Assert.Equal(0, capture.DiscardedRows);
        Assert.Equal(["Composed: Flip"], capture.PresentModes);
    }

    [Fact]
    public void CPUStartQPC_is_used_when_TimeInQPC_is_missing()
    {
        const string csv = """
            Application,ProcessID,SwapChainAddress,CPUStartQPC,MsBetweenPresents
            game.exe,1,0xA,500,16.6
            """;

        Assert.Equal(500, Assert.Single(ParseText(csv).Capture!.Frames).PresentQpc);
    }

    [Fact]
    public void Unknown_interval_column_is_unsupported_format()
    {
        var parse = ParseFile("pm-unknown-columns.synthetic.csv", "game.exe");

        Assert.Equal(FrameCaptureStatus.UnsupportedFormat, parse.Status);
        Assert.Null(parse.Capture);
        Assert.NotNull(parse.Detail);
    }

    [Theory]
    [InlineData("Application,SwapChainAddress,MsBetweenPresents\ngame.exe,0xA,16.6\n")]
    [InlineData("ProcessID,SwapChainAddress,MsBetweenPresents\n1,0xA,16.6\n")]
    [InlineData("Application,ProcessID,MsBetweenPresents\ngame.exe,1,16.6\n")]
    [InlineData("")]
    public void Missing_required_columns_or_empty_file_is_unsupported_format(string csv) =>
        Assert.Equal(FrameCaptureStatus.UnsupportedFormat, ParseText(csv).Status);

    [Fact]
    public void Header_only_means_the_target_was_not_found()
    {
        var parse = ParseFile("pm-header-only.csv", "game.exe");

        Assert.Equal(FrameCaptureStatus.TargetNotFound, parse.Status);
        Assert.Null(parse.Capture);
    }

    [Fact]
    public void Rows_of_other_processes_only_mean_the_target_was_not_found() =>
        Assert.Equal(FrameCaptureStatus.TargetNotFound, ParseFile(RealCapture, "game.exe").Status);

    [Fact]
    public void Swap_chain_with_most_frames_is_taken()
    {
        var capture = ParseFile("pm-two-swapchains.synthetic.csv", "game.exe").Capture!;

        Assert.Equal(300, capture.Frames.Count);
        Assert.All(capture.IntervalsMs(), ms => Assert.Equal(16.6667, ms, 10));
    }

    [Fact]
    public void Process_id_with_most_frames_is_taken_when_searching_by_name()
    {
        var capture = ParseFile("pm-two-pids.synthetic.csv", "game.exe").Capture!;

        Assert.Equal(200, capture.ProcessId);
        Assert.Equal(400, capture.Frames.Count);
    }

    [Fact]
    public void Process_id_filter_takes_exactly_that_process()
    {
        var capture = ParseFile("pm-two-pids.synthetic.csv", processName: null, processId: 100).Capture!;

        Assert.Equal(100, capture.ProcessId);
        Assert.Equal(50, capture.Frames.Count);
        Assert.Equal("game.exe", capture.ProcessName);
    }

    [Fact]
    public void Name_filter_ignores_case() =>
        Assert.Equal(FrameCaptureStatus.Completed, ParseFile(RealCapture, "FIREFOX.EXE").Status);

    [Fact]
    public void Na_empty_zero_negative_and_garbage_intervals_are_discarded()
    {
        const string csv = """
            Application,ProcessID,SwapChainAddress,MsBetweenPresents
            game.exe,1,0xA,16.6
            game.exe,1,0xA,NA
            game.exe,1,0xA,0
            game.exe,1,0xA,
            game.exe,1,0xA,-3.5
            game.exe,1,0xA,"16,6"
            game.exe,1,0xA,abc
            game.exe,1,0xA,1e1
            """;

        var capture = ParseText(csv).Capture!;

        Assert.Equal([16.6, 10.0], capture.IntervalsMs());
        Assert.Equal(6, capture.DiscardedRows);
    }

    [Fact]
    public void Only_discarded_rows_mean_no_frames()
    {
        var parse = ParseText("Application,ProcessID,SwapChainAddress,MsBetweenPresents\r\ngame.exe,1,0xA,NA\r\ngame.exe,1,0xA,0\r\n");

        Assert.Equal(FrameCaptureStatus.NoFrames, parse.Status);
        Assert.Null(parse.Capture);
        Assert.Contains("2", parse.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Numbers_are_read_the_same_under_russian_culture()
    {
        var invariant = ParseFile(RealCapture, "firefox.exe").Capture!.IntervalsMs();
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

            Assert.Equal(invariant, ParseFile(RealCapture, "firefox.exe").Capture!.IntervalsMs());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Bom_in_text_and_lf_line_endings_are_accepted()
    {
        var parse = ParseText("﻿Application,ProcessID,SwapChainAddress,MsBetweenPresents\ngame.exe,1,0xA,16.5\ngame.exe,1,0xA,17.5");

        Assert.Equal([16.5, 17.5], parse.Capture!.IntervalsMs());
    }

    [Fact]
    public void Quoted_fields_follow_rfc_4180()
    {
        const string csv = "Application,ProcessID,SwapChainAddress,MsBetweenPresents\r\n"
            + "\"My, \"\"Best\"\" Game.exe\",7,\"0xA\",\"16.5\"\r\n"
            + "\"My, \"\"Best\"\" Game.exe\",7,0xA,\"17.5\"\r\n";

        var capture = ParseText(csv, "My, \"Best\" Game.exe").Capture!;

        Assert.Equal("My, \"Best\" Game.exe", capture.ProcessName);
        Assert.Equal([16.5, 17.5], capture.IntervalsMs());
    }

    [Fact]
    public void Exactly_one_of_name_and_process_id_is_required()
    {
        Assert.Throws<ArgumentException>(() => ParseText("Application\n", processName: null, processId: null));
        Assert.Throws<ArgumentException>(() => ParseText("Application\n", processName: "game.exe", processId: 1));
    }
}
