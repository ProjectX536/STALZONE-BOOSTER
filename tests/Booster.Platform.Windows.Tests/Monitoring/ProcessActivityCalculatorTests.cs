using Booster.Core.Abstractions;
using Booster.Platform.Windows.Monitoring;

namespace Booster.Platform.Windows.Tests.Monitoring;

public class ProcessActivityCalculatorTests
{
    private const double Tolerance = 1e-9;
    private const long Second = 10_000_000; // тиков по 100 нс
    private const int Cpus = 8;

    private static ProcessTimes Idle(double seconds) => new(0, "Idle", 0, (long)(seconds * Second), [], ProcessCounters.Zero);

    private static ProcessTimes Process(int pid, string name, double seconds, long createTime = 1, params ThreadTimes[] threads) =>
        new(pid, name, createTime, (long)(seconds * Second), threads, ProcessCounters.Zero);

    private static ThreadTimes Thread(int id, double seconds) => new(id, (long)(seconds * Second));

    private static ProcessActivity Compute(IReadOnlyList<ProcessTimes>? previous, IReadOnlyList<ProcessTimes> current,
        string? name = null, int? pid = 10, int topCount = 5) =>
        ProcessActivityCalculator.Compute(previous, current, Second, Cpus, name, pid, topCount);

    [Fact]
    public void Busiest_thread_percent_is_thread_delta_over_elapsed()
    {
        var activity = Compute(
            [Idle(0), Process(10, "game.exe", 0, threads: [Thread(1, 0), Thread(2, 0)])],
            [Idle(0), Process(10, "game.exe", 1.2, threads: [Thread(1, 0.9), Thread(2, 0.3)])]);

        var target = activity.Target.Value!;
        Assert.Equal(DataStatus.Available, activity.Target.Status);
        Assert.Equal(90, target.BusiestThreadPercent, Tolerance);
        Assert.Equal(2, target.ThreadCount);
        Assert.Equal("game.exe", target.Name);
        Assert.Equal(10, target.ProcessId);
    }

    [Fact]
    public void Thread_started_during_interval_counts_all_its_time()
    {
        var activity = Compute(
            [Idle(0), Process(10, "game.exe", 0, threads: [Thread(1, 0)])],
            [Idle(0), Process(10, "game.exe", 0.6, threads: [Thread(1, 0.1), Thread(7, 0.5)])]);

        Assert.Equal(50, activity.Target.Value!.BusiestThreadPercent, Tolerance);
    }

    [Fact]
    public void Process_percent_is_over_all_logical_processors()
    {
        var activity = Compute([Idle(0), Process(10, "game.exe", 3)], [Idle(0), Process(10, "game.exe", 5)]);

        Assert.Equal(25, activity.Target.Value!.CpuPercent, Tolerance);
    }

    [Fact]
    public void System_cpu_is_one_minus_idle_share()
    {
        var activity = Compute([Idle(100)], [Idle(106)]);

        Assert.Equal(DataStatus.Available, activity.SystemCpuPercent.Status);
        Assert.Equal(25, activity.SystemCpuPercent.Value, Tolerance);
    }

    [Fact]
    public void First_sample_is_unknown_and_needs_a_second_one()
    {
        var activity = Compute(null, [Idle(0), Process(10, "game.exe", 1)]);

        Assert.Equal(DataStatus.Unknown, activity.SystemCpuPercent.Status);
        Assert.Equal(DataStatus.Unknown, activity.Target.Status);
        Assert.Equal(ProcessActivityCalculator.NeedsSecondSampleNote, activity.Target.Note);
        Assert.Empty(activity.TopOthers);
    }

    [Fact]
    public void Restarted_process_with_same_pid_is_new_target()
    {
        var activity = Compute([Idle(0), Process(10, "game.exe", 50, createTime: 1)], [Idle(0), Process(10, "game.exe", 1, createTime: 2)]);

        Assert.Equal(DataStatus.Unknown, activity.Target.Status);
        Assert.Equal(ProcessActivityCalculator.NeedsSecondSampleNote, activity.Target.Note);
    }

    [Fact]
    public void Top_others_exclude_target_and_idle_and_are_sorted()
    {
        var activity = Compute(
            [Idle(0), Process(10, "game.exe", 0), Process(20, "a.exe", 0), Process(30, "b.exe", 0), Process(40, "c.exe", 0)],
            [Idle(5), Process(10, "game.exe", 4), Process(20, "a.exe", 1), Process(30, "b.exe", 3), Process(40, "c.exe", 2)],
            topCount: 2);

        Assert.Equal([new ProcessCpuUsage("b.exe", 30, 37.5), new ProcessCpuUsage("c.exe", 40, 25)], activity.TopOthers);
    }

    [Fact]
    public void Process_that_appeared_since_previous_sample_is_left_out_of_top()
    {
        var activity = Compute([Idle(0), Process(10, "game.exe", 0)], [Idle(0), Process(10, "game.exe", 0), Process(20, "new.exe", 7)]);

        Assert.Empty(activity.TopOthers);
    }

    [Fact]
    public void Target_found_by_name_after_it_starts()
    {
        IReadOnlyList<ProcessTimes> before = [Idle(0), Process(20, "launcher.exe", 1)];
        IReadOnlyList<ProcessTimes> started = [Idle(1), Process(20, "launcher.exe", 1), Process(55, "Game.exe", 0.5)];
        IReadOnlyList<ProcessTimes> running = [Idle(2), Process(20, "launcher.exe", 1), Process(55, "Game.exe", 2.5)];

        var notYet = Compute(null, before, name: "game", pid: null);
        var firstSeen = Compute(before, started, name: "game", pid: null);
        var measured = Compute(started, running, name: "game", pid: null);

        Assert.Equal(DataStatus.Unknown, notYet.Target.Status);
        Assert.Equal(DataStatus.Unknown, firstSeen.Target.Status);
        Assert.Equal(ProcessActivityCalculator.NeedsSecondSampleNote, firstSeen.Target.Note);
        Assert.Equal(DataStatus.Available, measured.Target.Status);
        Assert.Equal(55, measured.Target.Value!.ProcessId);
        Assert.Equal(25, measured.Target.Value.CpuPercent, Tolerance);
    }

    [Fact]
    public void Target_not_running_is_unknown_with_reason()
    {
        var activity = Compute([Idle(0)], [Idle(1)], name: "game.exe", pid: null);

        Assert.Equal(DataStatus.Unknown, activity.Target.Status);
        Assert.Equal(ProcessActivityCalculator.TargetNotRunningNote, activity.Target.Note);
        Assert.Equal(DataStatus.Available, activity.SystemCpuPercent.Status);
    }

    [Fact]
    public void Name_match_ignores_case_and_exe_and_busiest_instance_wins()
    {
        var activity = Compute(
            [Idle(0), Process(10, "GAME.EXE", 0), Process(11, "game.exe", 0)],
            [Idle(0), Process(10, "GAME.EXE", 1), Process(11, "game.exe", 3)],
            name: "Game", pid: null);

        Assert.Equal(11, activity.Target.Value!.ProcessId);
        // Второй экземпляр с тем же именем — посторонний процесс.
        Assert.Equal([new ProcessCpuUsage("GAME.EXE", 10, 12.5)], activity.TopOthers);
    }

    [Fact]
    public void Exactly_one_of_name_and_pid_is_required()
    {
        Assert.Throws<ArgumentException>(() => Compute(null, [Idle(0)], name: null, pid: null));
        Assert.Throws<ArgumentException>(() => Compute(null, [Idle(0)], name: "game.exe", pid: 10));
    }
}
