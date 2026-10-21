using Microsoft.Extensions.Logging;

namespace Booster.Core.Tests.TestSupport;

public sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

public sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries
    {
        get { lock (_entries) return _entries.ToArray(); }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_entries) _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }
}
