using System.Diagnostics;
using System.Runtime.InteropServices;
using Booster.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.System.SystemInformation;

namespace Booster.Platform.Windows.Monitoring;

/// <summary>Поля <c>MEMORYSTATUSEX</c>, нужные для давления памяти, в байтах.</summary>
/// <param name="CommitLimit">ullTotalPageFile: предел выделения.</param>
/// <param name="CommitAvailable">ullAvailPageFile: сколько ещё можно выделить.</param>
public readonly record struct GlobalMemoryStatus(ulong TotalPhysical, ulong AvailablePhysical, ulong CommitLimit, ulong CommitAvailable);

/// <summary>
/// Давление памяти и диска, время DPC и прерываний, только чтение. Счётчики «Memory\Page Reads/sec», «PhysicalDisk(_Total)»
/// и «Processor(_Total)» открываются при первом вызове и живут между вызовами: все считаются по разности чтений,
/// первое чтение отбрасывается.
/// Счётчик, который не открылся, больше не открывается — его значение NotSupported с причиной.
/// </summary>
public sealed partial class SystemPressureSource(ILogger<SystemPressureSource> logger) : ISystemPressureSource, IDisposable
{
    public const string MemorySource = "GlobalMemoryStatusEx";
    public const string PageReadsSource = @"Performance counter Memory\Page Reads/sec";
    public const string DiskIdleSource = @"Performance counter PhysicalDisk(_Total)\% Idle Time";
    public const string DiskLatencySource = @"Performance counter PhysicalDisk(_Total)\Avg. Disk sec/Transfer";
    public const string DpcTimeSource = @"Performance counter Processor(_Total)\% DPC Time";
    public const string InterruptTimeSource = @"Performance counter Processor(_Total)\% Interrupt Time";
    public const string NeedsSecondReadingNote = "rate counters need a second reading";
    public const string ReadFailedNote = "counter cannot be read";
    public const string MemoryFailedNote = "GlobalMemoryStatusEx failed";

    private const string DiskCategory = "PhysicalDisk";
    private const string ProcessorCategory = "Processor";
    private const string TotalInstance = "_Total";

    private readonly Lock _lock = new();
    private Slot? _pageReads;
    private Slot? _diskIdle;
    private Slot? _diskLatency;
    private Slot? _dpcTime;
    private Slot? _interruptTime;
    private bool _readFailureLogged;
    private bool _disposed;

    public SystemPressure Sample()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var justOpened = _pageReads is null;
            _pageReads ??= Open(PageReadsSource, "Memory", "Page Reads/sec", string.Empty);
            _diskIdle ??= Open(DiskIdleSource, DiskCategory, "% Idle Time", TotalInstance);
            _diskLatency ??= Open(DiskLatencySource, DiskCategory, "Avg. Disk sec/Transfer", TotalInstance);
            _dpcTime ??= Open(DpcTimeSource, ProcessorCategory, "% DPC Time", TotalInstance);
            _interruptTime ??= Open(InterruptTimeSource, ProcessorCategory, "% Interrupt Time", TotalInstance);

            return Compute(ReadMemory(), Read(_pageReads, justOpened), Read(_diskIdle, justOpened), Read(_diskLatency, justOpened),
                Read(_dpcTime, justOpened), Read(_interruptTime, justOpened));
        }
    }

    /// <summary>Проценты памяти и пересчёт счётчиков. Не известное значение счётчика проходит как есть.</summary>
    /// <param name="memory">null — GlobalMemoryStatusEx не ответил.</param>
    public static SystemPressure Compute(GlobalMemoryStatus? memory, Reading<double> pageReadsPerSecond, Reading<double> diskIdlePercent,
        Reading<double> diskSecondsPerTransfer, Reading<double> dpcTimePercent, Reading<double> interruptTimePercent) =>
        new(memory is { } m ? Percent(m.AvailablePhysical, m.TotalPhysical) : Reading<double>.Failure(MemorySource, MemoryFailedNote),
            memory is { } c ? Percent(c.CommitLimit - Math.Min(c.CommitAvailable, c.CommitLimit), c.CommitLimit)
                : Reading<double>.Failure(MemorySource, MemoryFailedNote),
            Map(pageReadsPerSecond, value => Math.Max(0, value)),
            Map(diskIdlePercent, idle => Math.Clamp(100 - idle, 0, 100)),
            Map(diskSecondsPerTransfer, seconds => Math.Max(0, seconds * 1000)),
            Map(dpcTimePercent, ClampPercent),
            Map(interruptTimePercent, ClampPercent));

    /// <summary>Чтение счётчика: не открылся — NotSupported, только что открыт — Unknown, не прочитался — Failed.</summary>
    public static Reading<double> CounterReading(string source, string? openError, bool justOpened, double? value)
    {
        if (openError is not null)
            return Reading<double>.Unsupported(source, openError);
        if (justOpened)
            return Reading<double>.Unknown(source, NeedsSecondReadingNote);
        return value is { } known ? Reading<double>.Known(known, source) : Reading<double>.Failure(source, ReadFailedNote);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _pageReads?.Counter?.Dispose();
            _diskIdle?.Counter?.Dispose();
            _diskLatency?.Counter?.Dispose();
            _dpcTime?.Counter?.Dispose();
            _interruptTime?.Counter?.Dispose();
            _disposed = true;
        }
    }

    private static Reading<double> Percent(ulong part, ulong total) =>
        total == 0 ? Reading<double>.Unknown(MemorySource) : Reading<double>.Known(Math.Clamp(part * 100.0 / total, 0, 100), MemorySource);

    private static double ClampPercent(double value) => Math.Clamp(value, 0, 100);

    private static Reading<double> Map(Reading<double> reading, Func<double, double> convert) =>
        reading.IsAvailable ? reading with { Value = convert(reading.Value) } : reading;

    private GlobalMemoryStatus? ReadMemory()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (PInvoke.GlobalMemoryStatusEx(ref status))
            return new GlobalMemoryStatus(status.ullTotalPhys, status.ullAvailPhys, status.ullTotalPageFile, status.ullAvailPageFile);
        LogReadFailedOnce(MemorySource, Marshal.GetLastPInvokeErrorMessage());
        return null;
    }

    private Slot Open(string source, string category, string counterName, string instance)
    {
        PerformanceCounter? counter = null;
        try
        {
            counter = new PerformanceCounter(category, counterName, instance, readOnly: true);
            // Первое чтение проверяет, что счётчик есть, и задаёт отправную точку.
            counter.NextValue();
            return new Slot(source, counter, null);
        }
        catch (Exception ex)
        {
            counter?.Dispose();
            var reason = $"{ex.GetType().Name}: {ex.Message}";
            LogOpenFailed(source, reason);
            return new Slot(source, null, reason);
        }
    }

    private Reading<double> Read(Slot slot, bool justOpened)
    {
        double? value = null;
        if (slot.Counter is not null && !justOpened)
        {
            try
            {
                var raw = slot.Counter.NextValue();
                value = float.IsFinite(raw) ? raw : null;
            }
            catch (Exception ex)
            {
                LogReadFailedOnce(slot.Source, $"{ex.GetType().Name}: {ex.Message}");
            }
        }
        return CounterReading(slot.Source, slot.Error, justOpened, value);
    }

    private void LogReadFailedOnce(string source, string reason)
    {
        // Опрос идёт во время замера каждую секунду: одно и то же предупреждение повторять незачем.
        if (_readFailureLogged)
            return;
        _readFailureLogged = true;
        LogReadFailed(source, reason);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pressure counter '{Source}' cannot be opened: {Reason}")]
    private partial void LogOpenFailed(string source, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pressure source '{Source}' cannot be read: {Reason}")]
    private partial void LogReadFailed(string source, string reason);

    private sealed record Slot(string Source, PerformanceCounter? Counter, string? Error);
}
