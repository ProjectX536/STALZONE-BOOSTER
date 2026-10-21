using System.ComponentModel;
using Booster.Core.Abstractions;

namespace Booster.Core.Tests.TestSupport;

/// <summary>
/// Процессы в памяти: PID → имя, время старта и приоритет. Считает вызовы <see cref="List"/>.
/// <see cref="SetPriority"/> только запоминается: настоящие процессы не меняются.
/// </summary>
public sealed class FakeProcessProvider : IProcessProvider, IProcessPriorityController
{
    private readonly Dictionary<int, (string Name, DateTimeOffset StartTime, ProcessPriority Priority)> _processes = [];
    private readonly HashSet<int> _denied = [];
    private readonly HashSet<int> _unreadable = [];
    private readonly List<(ProcessIdentity Process, ProcessPriority Priority)> _priorityCalls = [];
    private int _listCalls;

    public int ListCalls => Volatile.Read(ref _listCalls);

    public IReadOnlyList<(ProcessIdentity Process, ProcessPriority Priority)> PriorityCalls
    {
        get { lock (_processes) return _priorityCalls.ToArray(); }
    }

    public ProcessIdentity Start(string name, int processId, DateTimeOffset? startTime = null, ProcessPriority priority = ProcessPriority.Normal)
    {
        var identity = new ProcessIdentity(processId, startTime ?? SessionFactory.Start);
        lock (_processes) _processes[processId] = (name, identity.StartTime, priority);
        return identity;
    }

    public void Exit(int processId)
    {
        lock (_processes) _processes.Remove(processId);
    }

    /// <summary>Приоритет сменил кто-то другой (сама игра или пользователь).</summary>
    public void ChangePriority(int processId, ProcessPriority priority)
    {
        lock (_processes) _processes[processId] = _processes[processId] with { Priority = priority };
    }

    /// <summary>Процесс запущен от имени администратора или защищён: менять приоритет нельзя.</summary>
    public void DenyAccess(int processId)
    {
        lock (_processes) _denied.Add(processId);
    }

    /// <summary>Процесс работает и есть в списке, но открыть его даже для чтения нельзя (например, защищён античитом).</summary>
    public void DenyRead(int processId)
    {
        lock (_processes) _unreadable.Add(processId);
    }

    public void AllowRead(int processId)
    {
        lock (_processes) _unreadable.Remove(processId);
    }

    public IReadOnlyList<ProcessInfo> List()
    {
        Interlocked.Increment(ref _listCalls);
        lock (_processes) return _processes.Select(p => new ProcessInfo(p.Key, p.Value.Name)).ToArray();
    }

    public ProcessIdentity? Identify(int processId)
    {
        lock (_processes)
            return _processes.TryGetValue(processId, out var process) && !_unreadable.Contains(processId)
                ? new ProcessIdentity(processId, process.StartTime)
                : null;
    }

    public ProcessPriority? GetPriority(ProcessIdentity process)
    {
        lock (_processes) return Readable(process) ? _processes[process.ProcessId].Priority : null;
    }

    public bool CanSetPriority(ProcessIdentity process)
    {
        lock (_processes) return Readable(process) && !_denied.Contains(process.ProcessId);
    }

    public void SetPriority(ProcessIdentity process, ProcessPriority priority)
    {
        if (priority == ProcessPriority.RealTime)
            throw new ArgumentOutOfRangeException(nameof(priority));
        lock (_processes)
        {
            if (!Matches(process))
                throw new InvalidOperationException($"Process {process.ProcessId} is not the one started at {process.StartTime:O}.");
            // Как ProcessProvider: ERROR_ACCESS_DENIED приходит как UnauthorizedAccessException.
            if (_denied.Contains(process.ProcessId) || _unreadable.Contains(process.ProcessId))
                throw new UnauthorizedAccessException("Access is denied.", new Win32Exception(5));
            _priorityCalls.Add((process, priority));
            _processes[process.ProcessId] = _processes[process.ProcessId] with { Priority = priority };
        }
    }

    private bool Matches(ProcessIdentity process) =>
        _processes.TryGetValue(process.ProcessId, out var running) && running.StartTime == process.StartTime;

    private bool Readable(ProcessIdentity process) => Matches(process) && !_unreadable.Contains(process.ProcessId);
}
