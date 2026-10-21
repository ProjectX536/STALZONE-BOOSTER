using Booster.Core.Abstractions;
using Booster.Core.Reports;
using Booster.Core.SystemHealth;

namespace Booster.Core.Tests.SystemHealth;

public class StabilityAnalyzerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private static StabilityEvent Event(StabilityEventKind kind, string? subject = null, int hoursAgo = 1) =>
        new(kind, Now.AddHours(-hoursAgo), "provider", 1, subject);

    private static StabilityLog Log(params StabilityEvent[] events) => new(Now.AddDays(-14), events, false);

    [Fact]
    public void Summary_counts_by_kind_with_last_time_and_top_subjects()
    {
        var log = Log(
            Event(StabilityEventKind.BugCheck, "0x9f", hoursAgo: 5),
            Event(StabilityEventKind.BugCheck, "0x124", hoursAgo: 1),
            Event(StabilityEventKind.BugCheck, "0x9f", hoursAgo: 10),
            Event(StabilityEventKind.UnexpectedShutdown, hoursAgo: 2),
            Event(StabilityEventKind.ApplicationCrash, "a.exe"),
            Event(StabilityEventKind.ApplicationCrash, "b.exe"),
            Event(StabilityEventKind.ApplicationCrash, "a.exe"),
            Event(StabilityEventKind.ApplicationCrash, "c.exe"),
            Event(StabilityEventKind.ApplicationCrash, "d.exe"),
            Event(StabilityEventKind.ApplicationCrash, "e.exe"),
            Event(StabilityEventKind.ApplicationCrash, "f.exe"),
            Event(StabilityEventKind.ApplicationCrash, " "),
            Event(StabilityEventKind.ApplicationCrash));

        var summary = StabilityAnalyzer.Summarize(log);

        Assert.Equal([StabilityEventKind.ApplicationCrash, StabilityEventKind.BugCheck, StabilityEventKind.UnexpectedShutdown],
            summary.Select(s => s.Kind));
        Assert.Equal(9, summary[0].Count);
        Assert.Equal(["a.exe", "b.exe", "c.exe", "d.exe", "e.exe"], summary[0].Subjects);
        Assert.Equal(3, summary[1].Count);
        Assert.Equal(Now.AddHours(-1), summary[1].Last);
        Assert.Equal(["0x9f", "0x124"], summary[1].Subjects);
        Assert.Empty(summary[2].Subjects);
        Assert.Equal(Now.AddHours(-2), summary[2].Last);
    }

    [Fact]
    public void No_events_no_advice()
    {
        Assert.Empty(StabilityAnalyzer.Summarize(Log()));
        Assert.Empty(StabilityAnalyzer.Advise(Log(), ["game.exe"]));
    }

    [Fact]
    public void Each_kind_gives_its_advice()
    {
        var log = Log(
            Event(StabilityEventKind.HardwareError),
            Event(StabilityEventKind.UnexpectedShutdown),
            Event(StabilityEventKind.UnexpectedShutdown),
            Event(StabilityEventKind.BugCheck, "0x9f"),
            Event(StabilityEventKind.BugCheck, "0x124"),
            Event(StabilityEventKind.DisplayDriverReset, "nvlddmkm"),
            Event(StabilityEventKind.DiskError));

        var advice = StabilityAnalyzer.Advise(log, []);

        Assert.Equal(
        [
            AdviceId.UnexpectedShutdowns, AdviceId.BugChecks, AdviceId.DisplayDriverResets, AdviceId.HardwareErrors, AdviceId.DiskErrors,
        ], advice.Select(a => a.Id));
        Assert.All(advice, a =>
        {
            Assert.Equal(AdviceModule.Stability, a.Module);
            Assert.Equal(FindingSeverity.Warning, a.Severity);
            Assert.Equal(SafetyLevel.L3ManualOnly, a.Level);
        });
        Assert.Equal(["2"], advice[0].Arguments);
        Assert.Equal(["2", "0x9f, 0x124"], advice[1].Arguments);
        Assert.Equal(["1"], advice[2].Arguments);
    }

    [Fact]
    public void Disk_and_ntfs_errors_are_combined()
    {
        var both = StabilityAnalyzer.Advise(Log(
            Event(StabilityEventKind.DiskError), Event(StabilityEventKind.DiskError), Event(StabilityEventKind.FileSystemError)), []);
        var ntfsOnly = StabilityAnalyzer.Advise(Log(Event(StabilityEventKind.FileSystemError)), []);

        Assert.Equal(["3"], Assert.Single(both, a => a.Id == AdviceId.DiskErrors).Arguments);
        Assert.Equal(["1"], Assert.Single(ntfsOnly, a => a.Id == AdviceId.DiskErrors).Arguments);
    }

    [Fact]
    public void Game_crash_matches_profile_exe_case_insensitively()
    {
        var log = Log(
            Event(StabilityEventKind.ApplicationCrash, "GAME.EXE"),
            Event(StabilityEventKind.ApplicationHang, "game.exe"),
            Event(StabilityEventKind.ApplicationCrash, "second.exe"),
            Event(StabilityEventKind.ApplicationCrash, "Other.exe"));

        var advice = Assert.Single(StabilityAnalyzer.Advise(log, ["Game.exe", "Second", "Unused.exe"]));

        Assert.Equal(AdviceId.GameCrashes, advice.Id);
        Assert.Equal(FindingSeverity.Warning, advice.Severity);
        Assert.Equal(["Game.exe, Second", "3"], advice.Arguments);
    }

    [Fact]
    public void Crash_of_non_game_gives_no_game_advice()
    {
        var log = Log(Event(StabilityEventKind.ApplicationCrash, "Other.exe"), Event(StabilityEventKind.ApplicationHang));

        Assert.Empty(StabilityAnalyzer.Advise(log, ["Game.exe"]));
        Assert.Empty(StabilityAnalyzer.Advise(log, []));
    }
}
