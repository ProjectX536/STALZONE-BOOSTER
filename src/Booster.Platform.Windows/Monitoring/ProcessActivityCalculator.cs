using Booster.Core.Abstractions;

namespace Booster.Platform.Windows.Monitoring;

/// <summary>
/// Загрузка CPU по двум снимкам процессов. Процесс узнаётся по паре PID + время запуска: PID переиспользуется.
/// Поток — по ID внутри процесса; поток, появившийся за интервал, засчитывается всем своим временем.
/// </summary>
public static class ProcessActivityCalculator
{
    public const string Source = "NtQuerySystemInformation(SystemProcessInformation)";
    public const string NeedsSecondSampleNote = "CPU load needs a second reading";
    public const string TargetNotRunningNote = "target process is not running";

    private const int IdleProcessId = 0;
    private const string ExeSuffix = ".exe";

    /// <param name="elapsedTicks100ns">Время между снимками в тиках по 100 нс.</param>
    /// <param name="processName">Имя цели без учёта регистра и «.exe»; из нескольких процессов — занявший больше CPU.</param>
    public static ProcessActivity Compute(IReadOnlyList<ProcessTimes>? previous, IReadOnlyList<ProcessTimes> current,
        long elapsedTicks100ns, int logicalProcessors, string? processName, int? processId, int topCount)
    {
        ArgumentNullException.ThrowIfNull(current);
        RequireOneTarget(processName, processId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(logicalProcessors);
        ArgumentOutOfRangeException.ThrowIfNegative(topCount);

        var candidates = current.Where(process => process.ProcessId != IdleProcessId
            && (processId is { } pid ? process.ProcessId == pid : SameName(process.Name, processName!))).ToList();

        if (previous is null || elapsedTicks100ns <= 0)
        {
            return new ProcessActivity(Reading<double>.Unknown(Source, NeedsSecondSampleNote),
                Reading<TargetCpuUsage>.Unknown(Source, candidates.Count == 0 ? TargetNotRunningNote : NeedsSecondSampleNote), []);
        }

        var before = new Dictionary<(int, long), ProcessTimes>();
        foreach (var process in previous)
            before.TryAdd(Key(process), process);

        var allProcessors = (double)elapsedTicks100ns * logicalProcessors;
        var target = candidates
            .Where(process => before.ContainsKey(Key(process)))
            .MaxBy(process => Delta(process, before[Key(process)]));

        var targetReading = target is not null
            ? Reading<TargetCpuUsage>.Known(Usage(target, before[Key(target)], elapsedTicks100ns, allProcessors), Source)
            : Reading<TargetCpuUsage>.Unknown(Source, candidates.Count == 0 ? TargetNotRunningNote : NeedsSecondSampleNote);

        var idle = current.FirstOrDefault(process => process.ProcessId == IdleProcessId);
        var systemReading = idle is not null && before.TryGetValue(Key(idle), out var idleBefore)
            ? Reading<double>.Known(Math.Clamp((1 - Delta(idle, idleBefore) / allProcessors) * 100, 0, 100), Source)
            : Reading<double>.Unknown(Source, NeedsSecondSampleNote);

        var others = current
            .Where(process => process.ProcessId != IdleProcessId && !ReferenceEquals(process, target) && before.ContainsKey(Key(process)))
            .Select(process => new ProcessCpuUsage(process.Name, process.ProcessId, Percent(Delta(process, before[Key(process)]), allProcessors)))
            .Where(usage => usage.CpuPercent > 0)
            .OrderByDescending(usage => usage.CpuPercent)
            .ThenBy(usage => usage.ProcessId)
            .Take(topCount)
            .ToArray();

        return new ProcessActivity(systemReading, targetReading, others);
    }

    /// <exception cref="ArgumentException">Заданы оба или ни одного.</exception>
    public static void RequireOneTarget(string? processName, int? processId)
    {
        if (string.IsNullOrWhiteSpace(processName) == processId is null)
            throw new ArgumentException("Exactly one of the process name and the process ID is required.", nameof(processName));
    }

    private static TargetCpuUsage Usage(ProcessTimes process, ProcessTimes before, long elapsedTicks100ns, double allProcessors)
    {
        var threadsBefore = new Dictionary<int, long>();
        foreach (var thread in before.Threads)
            threadsBefore.TryAdd(thread.ThreadId, thread.CpuTime100ns);

        var busiest = process.Threads
            .Select(thread => thread.CpuTime100ns - threadsBefore.GetValueOrDefault(thread.ThreadId))
            .DefaultIfEmpty(0)
            .Max();

        return new TargetCpuUsage(process.Name, process.ProcessId, Percent(Delta(process, before), allProcessors),
            Percent(busiest, elapsedTicks100ns), process.Threads.Count);
    }

    private static (int, long) Key(ProcessTimes process) => (process.ProcessId, process.CreateTime);

    private static double Delta(ProcessTimes process, ProcessTimes before) => Math.Max(0, process.CpuTime100ns - before.CpuTime100ns);

    private static double Percent(double ticks, double capacityTicks) => Math.Clamp(ticks / capacityTicks * 100, 0, 100);

    private static bool SameName(string name, string target) =>
        WithoutExe(name).Equals(WithoutExe(target.Trim()), StringComparison.OrdinalIgnoreCase);

    private static ReadOnlySpan<char> WithoutExe(string name) =>
        name.EndsWith(ExeSuffix, StringComparison.OrdinalIgnoreCase) ? name.AsSpan(0, name.Length - ExeSuffix.Length) : name;
}
