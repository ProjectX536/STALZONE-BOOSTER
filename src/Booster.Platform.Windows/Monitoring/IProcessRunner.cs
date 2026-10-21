namespace Booster.Platform.Windows.Monitoring;

public sealed record ProcessLaunch(string FileName, IReadOnlyList<string> Arguments, bool Elevate)
{
    public Action? Started { get; init; }
}

public interface IProcessRunner
{
    Task<int> RunAsync(ProcessLaunch launch, CancellationToken ct);
}
