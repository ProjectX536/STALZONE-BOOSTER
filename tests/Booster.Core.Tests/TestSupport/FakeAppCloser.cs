using Booster.Core.Abstractions;

namespace Booster.Core.Tests.TestSupport;

/// <summary>Запросы на закрытие только запоминаются: настоящие приложения не закрываются.</summary>
public sealed class FakeAppCloser : IAppCloser
{
    private readonly List<(string Name, TimeSpan Timeout)> _requests = [];

    /// <summary>Итог по имени; по умолчанию — <see cref="CloseRequestResult.Closed"/>.</summary>
    public Dictionary<string, CloseRequestResult> Results { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Имена, запрос по которым бросает исключение.</summary>
    public HashSet<string> Failing { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Вызывается в начале запроса.</summary>
    public Action<string>? OnRequest { get; set; }

    public IReadOnlyList<string> Requested
    {
        get { lock (_requests) return _requests.Select(r => r.Name).ToArray(); }
    }

    public IReadOnlyList<TimeSpan> Timeouts
    {
        get { lock (_requests) return _requests.Select(r => r.Timeout).ToArray(); }
    }

    public Task<CloseRequestResult> RequestCloseAsync(string processName, TimeSpan timeout, CancellationToken ct)
    {
        lock (_requests) _requests.Add((processName, timeout));
        OnRequest?.Invoke(processName);
        ct.ThrowIfCancellationRequested();
        if (Failing.Contains(processName))
            throw new InvalidOperationException($"close {processName} failed");
        return Task.FromResult(Results.GetValueOrDefault(processName, CloseRequestResult.Closed));
    }
}
