using Booster.Core.Abstractions;
using Booster.Platform.Windows.Monitoring;

namespace Booster.Platform.Windows.Tests.Monitoring;

public class StabilityQueriesTests
{
    private static readonly DateTimeOffset Since = new(2026, 10, 6, 15, 30, 0, TimeSpan.FromHours(3));

    [Fact]
    public void System_xpath_contains_time_filter_in_utc()
    {
        var system = StabilityQueries.SystemXPath(Since);
        var application = StabilityQueries.ApplicationXPath(Since);

        Assert.Contains("TimeCreated[@SystemTime>='2026-10-06T12:30:00.000Z']", system, StringComparison.Ordinal);
        Assert.Contains("(Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=41))", system, StringComparison.Ordinal);
        Assert.Contains("(Provider[@Name='Microsoft-Windows-WHEA-Logger'] and (Level=1 or Level=2 or Level=3))", system,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Application Error", system, StringComparison.Ordinal);

        Assert.Contains("TimeCreated[@SystemTime>='2026-10-06T12:30:00.000Z']", application, StringComparison.Ordinal);
        Assert.Contains("(Provider[@Name='Application Error'] and (EventID=1000))", application, StringComparison.Ordinal);
        Assert.DoesNotContain("Kernel-Power", application, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Microsoft-Windows-Kernel-Power", 41, (byte)1, StabilityEventKind.UnexpectedShutdown)]
    [InlineData("Microsoft-Windows-WER-SystemErrorReporting", 1001, (byte)2, StabilityEventKind.BugCheck)]
    [InlineData("Display", 4101, (byte)3, StabilityEventKind.DisplayDriverReset)]
    [InlineData("Microsoft-Windows-WHEA-Logger", 18, (byte)1, StabilityEventKind.HardwareError)]
    [InlineData("Microsoft-Windows-WHEA-Logger", 47, (byte)3, StabilityEventKind.HardwareError)]
    [InlineData("disk", 7, (byte)2, StabilityEventKind.DiskError)]
    [InlineData("disk", 11, (byte)2, StabilityEventKind.DiskError)]
    [InlineData("disk", 51, (byte)3, StabilityEventKind.DiskError)]
    [InlineData("disk", 153, (byte)3, StabilityEventKind.DiskError)]
    [InlineData("stornvme", 129, (byte)3, StabilityEventKind.DiskError)]
    [InlineData("storahci", 129, (byte)3, StabilityEventKind.DiskError)]
    [InlineData("Microsoft-Windows-Ntfs", 55, (byte)2, StabilityEventKind.FileSystemError)]
    [InlineData("Application Error", 1000, (byte)2, StabilityEventKind.ApplicationCrash)]
    [InlineData("Application Hang", 1002, (byte)2, StabilityEventKind.ApplicationHang)]
    [InlineData("DISK", 7, (byte)2, StabilityEventKind.DiskError)]
    public void Classify_maps_known_events(string provider, int id, byte level, StabilityEventKind expected)
    {
        Assert.Equal(expected, StabilityQueries.Classify(provider, id, level));
    }

    [Fact]
    public void Classify_ignores_other_events()
    {
        Assert.Null(StabilityQueries.Classify("Microsoft-Windows-Kernel-Power", 42, 4));
        Assert.Null(StabilityQueries.Classify("disk", 8, 2));
        Assert.Null(StabilityQueries.Classify("Contoso", 41, 1));
    }

    [Fact]
    public void Whea_info_level_is_ignored()
    {
        Assert.Null(StabilityQueries.Classify("Microsoft-Windows-WHEA-Logger", 47, 4));
        Assert.Null(StabilityQueries.Classify("Microsoft-Windows-WHEA-Logger", 47, 0));
        Assert.Null(StabilityQueries.Classify("Microsoft-Windows-WHEA-Logger", 47, null));
    }

    [Fact]
    public void Subject_is_trimmed_to_200_chars()
    {
        var time = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);

        var longSubject = StabilityQueries.ToEvent(new StabilityRecord("Display", 4101, 3, time, new string('x', 300)));
        var blank = StabilityQueries.ToEvent(new StabilityRecord("Display", 4101, 3, time, "  "));

        Assert.Equal(new string('x', 200), longSubject!.Subject);
        Assert.Equal(new StabilityEvent(StabilityEventKind.DisplayDriverReset, time, "Display", 4101, null), blank);
        Assert.Null(StabilityQueries.ToEvent(new StabilityRecord("Contoso", 1, 2, time, "x")));
    }

    [Fact]
    public void Bug_check_subject_is_the_code()
    {
        var record = new StabilityRecord("Microsoft-Windows-WER-SystemErrorReporting", 1001, 2, DateTimeOffset.UnixEpoch,
            "0x0000009f (0x0000000000000003, 0xffffb00c8f2a6060, 0xfffff8065f467750, 0xffffb00c96ab08a0)");

        Assert.Equal("0x0000009f", StabilityQueries.ToEvent(record)!.Subject);
    }
}
