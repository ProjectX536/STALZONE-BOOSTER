namespace Booster.Core.Abstractions;

public enum WprOutcome
{
    Done,

    Declined,

    Failed,
}

public sealed record WprResult(WprOutcome Outcome, int? ExitCode = null, string? Detail = null, string? Path = null);

public interface IWprRunner
{
    bool IsAvailable { get; }

    Task<WprResult> StartAsync(CancellationToken ct);

    Task<WprResult> StopAsync(string fileName, CancellationToken ct);

    Task<WprResult> CancelAsync(CancellationToken ct);
}
