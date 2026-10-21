using Booster.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Booster.Platform.Windows.Monitoring;

/// <summary>
/// Память и ввод-вывод процессов через <c>NtQuerySystemInformation</c>: снимок всей системы, без открытия процессов.
/// У источника свой буфер и свой прошлый снимок, поэтому он не мешает замерам CPU.
/// </summary>
public sealed partial class ProcessResourceSource : IProcessResourceSource, IDisposable
{
    private const string NotSupportedNote = "process snapshot layout is known only for 64-bit processes";
    private const string NeedsSecondSampleNote = "I/O rates need a second reading";

    private readonly TimeProvider _time;
    private readonly ILogger<ProcessResourceSource> _logger;
    private readonly Lock _lock = new();
    private readonly ProcessSnapshotReader _reader = new();
    private IReadOnlyList<ProcessTimes>? _previous;
    private long _previousTimestamp;
    private bool _readFailureLogged;
    private bool _disposed;

    public ProcessResourceSource(TimeProvider time, ILogger<ProcessResourceSource> logger)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _time = time;
        _logger = logger;
    }

    public IReadOnlyList<ProcessMemoryUsage> ReadMemory(int topCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(topCount);
        if (!Environment.Is64BitProcess)
            throw new PlatformNotSupportedException(NotSupportedNote);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return ProcessResourceCalculator.Memory(_reader.Read(), topCount);
        }
    }

    public Reading<ProcessIoActivity> SampleIo(int topCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(topCount);
        if (!Environment.Is64BitProcess)
            return Reading<ProcessIoActivity>.Unsupported(ProcessActivityCalculator.Source, NotSupportedNote);

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
                if (!_readFailureLogged)
                {
                    _readFailureLogged = true;
                    LogReadFailed(exception.Message);
                }
                return Reading<ProcessIoActivity>.Failure(ProcessActivityCalculator.Source, exception.Message);
            }
            var timestamp = _time.GetTimestamp();

            var previous = _previous;
            var elapsed = previous is null ? TimeSpan.Zero : _time.GetElapsedTime(_previousTimestamp, timestamp);
            _previous = current;
            _previousTimestamp = timestamp;

            return previous is null || elapsed <= TimeSpan.Zero
                ? Reading<ProcessIoActivity>.Unknown(ProcessActivityCalculator.Source, NeedsSecondSampleNote)
                : Reading<ProcessIoActivity>.Known(ProcessResourceCalculator.Io(previous, current, elapsed, topCount),
                    ProcessActivityCalculator.Source);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Process memory and I/O snapshot is unavailable: {Reason}")]
    private partial void LogReadFailed(string reason);
}
