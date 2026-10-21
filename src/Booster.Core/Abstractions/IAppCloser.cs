namespace Booster.Core.Abstractions;

public enum CloseRequestResult
{
    Closed,

    StillRunning,

    NoWindow,

    NotRunning,
}

public interface IAppCloser
{
    Task<CloseRequestResult> RequestCloseAsync(string processName, TimeSpan timeout, CancellationToken ct);
}
