using Booster.Core.Abstractions;
using Booster.Platform.Windows.Monitoring;

namespace Booster.Platform.Windows.Tests.Monitoring;

public class ProcessResourceCalculatorTests
{
    private static ProcessTimes P(int pid, string name, long create = 1, long workingSet = 0, long privateBytes = 0, long faults = 0,
        long read = 0, long write = 0) =>
        new(pid, name, create, 0, [], new ProcessCounters(workingSet, privateBytes, faults, read, write));

    [Fact]
    public void Memory_is_sorted_by_private_bytes_and_excludes_idle()
    {
        IReadOnlyList<ProcessTimes> snapshot =
        [
            P(0, "Idle", privateBytes: 999_999_999),
            P(10, "a.exe", workingSet: 50, privateBytes: 100),
            P(11, "b.exe", workingSet: 70, privateBytes: 300),
            P(12, "c.exe", workingSet: 60, privateBytes: 200),
        ];

        var top = ProcessResourceCalculator.Memory(snapshot, 2);

        Assert.Equal([new ProcessMemoryUsage("b.exe", 11, 70, 300), new ProcessMemoryUsage("c.exe", 12, 60, 200)], top);
    }

    [Fact]
    public void Io_is_rate_per_second_between_samples()
    {
        IReadOnlyList<ProcessTimes> previous = [P(10, "game.exe", create: 5)];
        IReadOnlyList<ProcessTimes> current = [P(10, "game.exe", create: 5, faults: 10, read: 2_000_000, write: 1_000_000)];

        var io = ProcessResourceCalculator.Io(previous, current, TimeSpan.FromSeconds(2), 5);

        var expected = new ProcessIoUsage("game.exe", 10, ReadBytesPerSecond: 1_000_000, WriteBytesPerSecond: 500_000, HardFaultsPerSecond: 5);
        Assert.Equal([expected], io.TopIo);
        Assert.Equal([expected], io.TopHardFaults);
    }

    [Fact]
    public void Restarted_process_with_same_pid_is_not_paired()
    {
        IReadOnlyList<ProcessTimes> previous = [P(10, "game.exe", create: 5)];
        IReadOnlyList<ProcessTimes> current = [P(10, "game.exe", create: 6, faults: 10, read: 2_000_000)];

        var io = ProcessResourceCalculator.Io(previous, current, TimeSpan.FromSeconds(2), 5);

        Assert.Empty(io.TopIo);
        Assert.Empty(io.TopHardFaults);
    }

    [Fact]
    public void Top_lists_are_limited_and_sorted()
    {
        IReadOnlyList<ProcessTimes> previous = [P(1, "a.exe"), P(2, "b.exe"), P(3, "c.exe")];
        IReadOnlyList<ProcessTimes> current =
        [
            P(1, "a.exe", read: 200, write: 100, faults: 1),
            P(2, "b.exe", read: 100, faults: 30),
            P(3, "c.exe", write: 200, faults: 20),
        ];

        var io = ProcessResourceCalculator.Io(previous, current, TimeSpan.FromSeconds(1), 2);

        Assert.Equal(["a.exe", "c.exe"], io.TopIo.Select(p => p.Name));
        Assert.Equal(["b.exe", "c.exe"], io.TopHardFaults.Select(p => p.Name));
    }

    [Fact]
    public void Processes_without_activity_and_idle_are_not_listed()
    {
        IReadOnlyList<ProcessTimes> previous = [P(0, "Idle"), P(1, "quiet.exe", read: 100, faults: 3)];
        IReadOnlyList<ProcessTimes> current = [P(0, "Idle", read: 500, faults: 9), P(1, "quiet.exe", read: 100, faults: 3)];

        var io = ProcessResourceCalculator.Io(previous, current, TimeSpan.FromSeconds(1), 5);

        Assert.Empty(io.TopIo);
        Assert.Empty(io.TopHardFaults);
    }

    [Fact]
    public void Decreasing_counter_is_treated_as_zero()
    {
        IReadOnlyList<ProcessTimes> previous = [P(1, "a.exe", read: 1000, write: 50)];
        IReadOnlyList<ProcessTimes> current = [P(1, "a.exe", read: 10, write: 150)];

        var io = ProcessResourceCalculator.Io(previous, current, TimeSpan.FromSeconds(1), 5);

        var usage = Assert.Single(io.TopIo);
        Assert.Equal((0, 100), (usage.ReadBytesPerSecond, usage.WriteBytesPerSecond));
    }
}
