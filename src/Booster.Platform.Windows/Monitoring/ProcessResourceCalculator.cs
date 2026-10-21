using Booster.Core.Abstractions;

namespace Booster.Platform.Windows.Monitoring;

internal static class ProcessResourceCalculator
{
    private const int IdleProcessId = 0;

    public static IReadOnlyList<ProcessMemoryUsage> Memory(IReadOnlyList<ProcessTimes> snapshot, int topCount)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(topCount);

        return snapshot
            .Where(process => process.ProcessId != IdleProcessId)
            .OrderByDescending(process => process.Counters.PrivateBytes)
            .ThenBy(process => process.ProcessId)
            .Take(topCount)
            .Select(process => new ProcessMemoryUsage(process.Name, process.ProcessId, process.Counters.WorkingSetBytes,
                process.Counters.PrivateBytes))
            .ToArray();
    }

    public static ProcessIoActivity Io(IReadOnlyList<ProcessTimes> previous, IReadOnlyList<ProcessTimes> current, TimeSpan elapsed,
        int topCount)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(elapsed, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(topCount);

        var before = new Dictionary<(int, long), ProcessCounters>();
        foreach (var process in previous)
            before.TryAdd((process.ProcessId, process.CreateTime), process.Counters);

        var seconds = elapsed.TotalSeconds;
        var usages = current
            .Where(process => process.ProcessId != IdleProcessId)
            .Select(process => before.TryGetValue((process.ProcessId, process.CreateTime), out var old)
                ? new ProcessIoUsage(process.Name, process.ProcessId,
                    Rate(process.Counters.ReadBytes, old.ReadBytes, seconds),
                    Rate(process.Counters.WriteBytes, old.WriteBytes, seconds),
                    Rate(process.Counters.HardFaultCount, old.HardFaultCount, seconds))
                : null)
            .OfType<ProcessIoUsage>()
            .ToList();

        var topIo = usages
            .Where(usage => usage.ReadBytesPerSecond + usage.WriteBytesPerSecond > 0)
            .OrderByDescending(usage => usage.ReadBytesPerSecond + usage.WriteBytesPerSecond)
            .ThenBy(usage => usage.ProcessId)
            .Take(topCount)
            .ToArray();
        var topHardFaults = usages
            .Where(usage => usage.HardFaultsPerSecond > 0)
            .OrderByDescending(usage => usage.HardFaultsPerSecond)
            .ThenBy(usage => usage.ProcessId)
            .Take(topCount)
            .ToArray();
        return new ProcessIoActivity(topIo, topHardFaults);
    }

    private static double Rate(long now, long before, double seconds) => Math.Max(0, now - before) / seconds;
}
