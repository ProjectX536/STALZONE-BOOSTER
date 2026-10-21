using Booster.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Windows.Win32;

namespace Booster.Platform.Windows.Monitoring;

public sealed partial class ProcessActivitySource : IProcessActivitySource, IDisposable
{
    private const ushort AllProcessorGroups = 0xFFFF;
    private const string NotSupportedNote = "process snapshot layout is known only for 64-bit processes";

    private readonly TimeProvider _time;
    private readonly ILogger<ProcessActivitySource> _logger;
    private readonly int _logicalProcessors;
    private readonly Lock _lock = new();
    private readonly ProcessSnapshotReader _reader = new();
    private IReadOnlyList<ProcessTimes>? _previous;
    private long _previousTimestamp;
    private bool _readFailureLogged;
    private bool _disposed;

    public ProcessActivitySource(TimeProvider time, ILogger<ProcessActivitySource> logger)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _time = time;
        _logger = logger;
        var active = (int)PInvoke.GetActiveProcessorCount(AllProcessorGroups);
        _logicalProcessors = active > 0 ? active : Environment.ProcessorCount;
    }

    public ProcessActivity Sample(string? processName, int? processId, int topCount)
    {
        ProcessActivityCalculator.RequireOneTarget(processName, processId);
        ArgumentOutOfRangeException.ThrowIfNegative(topCount);

        if (!Environment.Is64BitProcess)
            return Unavailable(DataStatus.NotSupported, NotSupportedNote);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            IReadOnlyList<ProcessTimes> current;
            try
            {
                current = _reader.Read();
            }
            catch (Exception exception) when (exception is InvalidDataException or OutOfMemoryException)
            {
                return Fail(exception.Message);
            }
            var timestamp = _time.GetTimestamp();

            var activity = ProcessActivityCalculator.Compute(_previous, current,
                _previous is null ? 0 : _time.GetElapsedTime(_previousTimestamp, timestamp).Ticks,
                _logicalProcessors, processName, processId, topCount);
            _previous = current;
            _previousTimestamp = timestamp;
            return activity;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _reader.Dispose();
            _previous = null;
            _disposed = true;
        }
    }

    private ProcessActivity Fail(string reason)
    {
        if (!_readFailureLogged)
        {
            _readFailureLogged = true;
            LogReadFailed(reason);
        }
        return Unavailable(DataStatus.Failed, reason);
    }

    private static ProcessActivity Unavailable(DataStatus status, string note) =>
        new(new Reading<double>(default, status, ProcessActivityCalculator.Source, note),
            new Reading<TargetCpuUsage>(default, status, ProcessActivityCalculator.Source, note), []);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Process CPU snapshot is unavailable: {Reason}")]
    private partial void LogReadFailed(string reason);
}
