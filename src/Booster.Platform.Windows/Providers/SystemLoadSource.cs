using System.ComponentModel;
using System.Runtime.InteropServices;
using Booster.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.System.SystemInformation;

namespace Booster.Platform.Windows.Providers;

/// <summary>
/// CPU — по разности <c>GetSystemTimes</c> между вызовами, память — <c>GlobalMemoryStatusEx.dwMemoryLoad</c>.
/// Оба вызова дешёвые и не открывают другие процессы.
/// </summary>
public sealed partial class SystemLoadSource(ILogger<SystemLoadSource> logger) : ISystemLoadSource
{
    private readonly Lock _gate = new();
    private CpuTimes? _previous;
    private int _failureLogged;

    public SystemLoad Sample() => new(SampleCpu(), SampleMemory());

    private double? SampleCpu()
    {
        if (!PInvoke.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            LogFailureOnce(nameof(PInvoke.GetSystemTimes));
            return null;
        }

        var current = new CpuTimes(Ticks(idle), Ticks(kernel), Ticks(user));
        lock (_gate)
        {
            var previous = _previous;
            _previous = current;
            if (previous is null)
                return null;

            // Время ядра включает время простоя.
            var total = current.Kernel - previous.Kernel + (current.User - previous.User);
            if (total <= 0)
                return null;
            var busy = total - (current.Idle - previous.Idle);
            return Math.Clamp(busy * 100.0 / total, 0, 100);
        }
    }

    private double? SampleMemory()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!PInvoke.GlobalMemoryStatusEx(ref status))
        {
            LogFailureOnce(nameof(PInvoke.GlobalMemoryStatusEx));
            return null;
        }

        return status.dwMemoryLoad;
    }

    private static long Ticks(System.Runtime.InteropServices.ComTypes.FILETIME time) =>
        ((long)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime;

    // Замер идёт каждые 1–2 с: повторять одно и то же предупреждение незачем.
    private void LogFailureOnce(string function)
    {
        if (Interlocked.Exchange(ref _failureLogged, 1) == 0)
            LogFailed(function, new Win32Exception(Marshal.GetLastPInvokeError()).Message);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Function} failed: {Error}; system load is unavailable")]
    private partial void LogFailed(string function, string error);

    private sealed record CpuTimes(long Idle, long Kernel, long User);
}
