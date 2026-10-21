using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Booster.Core.Abstractions;
using Booster.Core.Profiles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Booster.Platform.Windows.Providers;

/// <summary>
/// Список процессов, опознание по PID и времени старта, приоритет, штатное закрытие. Меняют систему только
/// <see cref="SetPriority"/> и <see cref="RequestCloseAsync"/>.
/// </summary>
public sealed class ProcessProvider : IProcessProvider, IProcessPriorityController, IAppCloser
{
    // PROCESS_QUERY_LIMITED_INFORMATION выдаётся и для чужих процессов того же пользователя; процесс только читается.
    private const PROCESS_ACCESS_RIGHTS QueryAccess = PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION;
    private const PROCESS_ACCESS_RIGHTS SetAccess = PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_ACCESS_RIGHTS.PROCESS_SET_INFORMATION;

    // STILL_ACTIVE (= STATUS_PENDING): код выхода, который GetExitCodeProcess возвращает для работающего процесса.
    private const uint StillActive = 259;

    // Как часто проверяется, вышло ли закрываемое приложение: выход после WM_CLOSE занимает от долей секунды до нескольких
    // секунд, а опрос списка процессов во время игры должен быть редким. 250 мс — задержка итога не больше четверти секунды.
    private static readonly TimeSpan ClosePollInterval = TimeSpan.FromMilliseconds(250);

    public IReadOnlyList<ProcessInfo> List()
    {
        var processes = Process.GetProcesses();
        try
        {
            var result = new List<ProcessInfo>(processes.Length);
            foreach (var process in processes)
            {
                try
                {
                    // Id и ProcessName берутся из снимка списка процессов; сам процесс не открывается.
                    result.Add(new ProcessInfo(process.Id, process.ProcessName));
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    // Процесс завершился между перечислением и чтением свойств.
                }
            }

            return result;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    public ProcessIdentity? Identify(int processId)
    {
        if (processId <= 0)
            return null;

        using var handle = PInvoke.OpenProcess_SafeHandle(QueryAccess, false, (uint)processId);
        return !handle.IsInvalid && IsRunning(handle) && StartTime(handle) is { } startTime ? new ProcessIdentity(processId, startTime) : null;
    }

    public ProcessPriority? GetPriority(ProcessIdentity process)
    {
        using var handle = Open(process, QueryAccess);
        if (handle is null)
            return null;
        var priorityClass = PInvoke.GetPriorityClass(handle);
        return priorityClass == 0 ? null : FromClass((PROCESS_CREATION_FLAGS)priorityClass);
    }

    public bool CanSetPriority(ProcessIdentity process)
    {
        using var handle = Open(process, SetAccess);
        return handle is not null;
    }

    public void SetPriority(ProcessIdentity process, ProcessPriority priority)
    {
        if (priority is ProcessPriority.RealTime || !Enum.IsDefined(priority))
            throw new ArgumentOutOfRangeException(nameof(priority), priority, "Only Idle..High can be set.");
        if (process.ProcessId <= 0)
            throw new InvalidOperationException($"Process {process.ProcessId} does not exist.");

        // Один дескриптор на сверку времени старта и на изменение: между ними PID не может достаться другому процессу.
        using var handle = PInvoke.OpenProcess_SafeHandle(SetAccess, false, (uint)process.ProcessId);
        if (handle.IsInvalid)
            throw PriorityError(Marshal.GetLastPInvokeError());
        if (StartTime(handle) != process.StartTime)
            throw new InvalidOperationException($"Process {process.ProcessId} is not the one started at {process.StartTime:O}.");
        if (!PInvoke.SetPriorityClass(handle, ToClass(priority)))
            throw PriorityError(Marshal.GetLastPInvokeError());
    }

    public async Task<CloseRequestResult> RequestCloseAsync(string processName, TimeSpan timeout, CancellationToken ct)
    {
        // Повторяет отбор BackgroundAppsAction: даже при ошибке в Core защищённый процесс не получит WM_CLOSE.
        var name = ProcessNames.Normalize(processName) ?? throw new ArgumentException("Not a process name.", nameof(processName));
        if (ProtectedProcesses.IsProtected(name))
            throw new ArgumentException($"Process {name} is protected and is never closed.", nameof(processName));
        ct.ThrowIfCancellationRequested();

        var processIds = ProcessIds(name);
        if (processIds.Count == 0)
            return CloseRequestResult.NotRunning;
        var windows = MainWindows(processIds);
        if (windows.Count == 0)
            return CloseRequestResult.NoWindow;

        // Как кнопка «×»: приложение само решает, выйти ли (может спросить о несохранённом или свернуться в трей).
        foreach (var window in windows)
            PInvoke.PostMessage(window, PInvoke.WM_CLOSE, default, default);

        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < timeout)
        {
            await Task.Delay(ClosePollInterval, ct).ConfigureAwait(false);
            if (ProcessIds(name).Count == 0)
                return CloseRequestResult.Closed;
        }

        return ProcessIds(name).Count == 0 ? CloseRequestResult.Closed : CloseRequestResult.StillRunning;
    }

    /// <summary>
    /// Отказ в доступе — <see cref="UnauthorizedAccessException"/> (контракт <see cref="IProcessPriorityController.SetPriority"/>):
    /// действие пропускается, а не откатывает сессию. Остальные коды — <see cref="Win32Exception"/>.
    /// </summary>
    internal static Exception PriorityError(int error)
    {
        var win32 = new Win32Exception(error);
        return error == (int)WIN32_ERROR.ERROR_ACCESS_DENIED ? new UnauthorizedAccessException(win32.Message, win32) : win32;
    }

    /// <summary>PID процессов с этим именем, кроме собственного и защищённых (<see cref="CloseTargetFilter"/>).</summary>
    private static HashSet<uint> ProcessIds(string name)
    {
        var processes = Process.GetProcessesByName(name);
        try
        {
            var candidates = new List<(string Name, int ProcessId)>(processes.Length);
            foreach (var process in processes)
            {
                try
                {
                    candidates.Add((process.ProcessName, process.Id));
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    // Процесс завершился между перечислением и чтением свойств.
                }
            }

            return CloseTargetFilter.Filter(candidates, Environment.ProcessId).Select(id => (uint)id).ToHashSet();
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    /// <summary>Видимые окна верхнего уровня без владельца — те, что пользователь закрывает кнопкой «×».</summary>
    private static List<HWND> MainWindows(HashSet<uint> processIds)
    {
        var windows = new List<HWND>();
        PInvoke.EnumWindows((window, _) =>
        {
            PInvoke.GetWindowThreadProcessId(window, out var processId);
            if (processIds.Contains(processId) && PInvoke.IsWindowVisible(window) && PInvoke.GetWindow(window, GET_WINDOW_CMD.GW_OWNER).IsNull)
                windows.Add(window);
            return true;
        }, default);
        return windows;
    }

    /// <returns>null — процесса нет, нет доступа или PID принадлежит другому процессу.</returns>
    private static SafeHandle? Open(ProcessIdentity process, PROCESS_ACCESS_RIGHTS access)
    {
        if (process.ProcessId <= 0)
            return null;
        var handle = PInvoke.OpenProcess_SafeHandle(access, false, (uint)process.ProcessId);
        if (!handle.IsInvalid && IsRunning(handle) && StartTime(handle) == process.StartTime)
            return handle;
        handle.Dispose();
        return null;
    }

    // Завершённый процесс открывается по PID, пока кто-то держит его дескриптор (лаунчер — дескриптор игры).
    // Процесс, сам вышедший с кодом 259, тоже сочтётся работающим — как и в GetExitCodeProcess.
    private static bool IsRunning(SafeHandle handle) => PInvoke.GetExitCodeProcess(handle, out var exitCode) && exitCode == StillActive;

    private static DateTimeOffset? StartTime(SafeHandle handle)
    {
        if (!PInvoke.GetProcessTimes(handle, out var creation, out _, out _, out _))
            return null;
        var fileTime = ((long)creation.dwHighDateTime << 32) | (uint)creation.dwLowDateTime;
        return DateTimeOffset.FromFileTime(fileTime);
    }

    private static ProcessPriority? FromClass(PROCESS_CREATION_FLAGS priorityClass) => priorityClass switch
    {
        PROCESS_CREATION_FLAGS.IDLE_PRIORITY_CLASS => ProcessPriority.Idle,
        PROCESS_CREATION_FLAGS.BELOW_NORMAL_PRIORITY_CLASS => ProcessPriority.BelowNormal,
        PROCESS_CREATION_FLAGS.NORMAL_PRIORITY_CLASS => ProcessPriority.Normal,
        PROCESS_CREATION_FLAGS.ABOVE_NORMAL_PRIORITY_CLASS => ProcessPriority.AboveNormal,
        PROCESS_CREATION_FLAGS.HIGH_PRIORITY_CLASS => ProcessPriority.High,
        PROCESS_CREATION_FLAGS.REALTIME_PRIORITY_CLASS => ProcessPriority.RealTime,
        _ => null,
    };

    private static PROCESS_CREATION_FLAGS ToClass(ProcessPriority priority) => priority switch
    {
        ProcessPriority.Idle => PROCESS_CREATION_FLAGS.IDLE_PRIORITY_CLASS,
        ProcessPriority.BelowNormal => PROCESS_CREATION_FLAGS.BELOW_NORMAL_PRIORITY_CLASS,
        ProcessPriority.Normal => PROCESS_CREATION_FLAGS.NORMAL_PRIORITY_CLASS,
        ProcessPriority.AboveNormal => PROCESS_CREATION_FLAGS.ABOVE_NORMAL_PRIORITY_CLASS,
        ProcessPriority.High => PROCESS_CREATION_FLAGS.HIGH_PRIORITY_CLASS,
        _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, null),
    };
}
