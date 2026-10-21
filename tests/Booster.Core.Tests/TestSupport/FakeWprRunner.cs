using Booster.Core.Abstractions;

namespace Booster.Core.Tests.TestSupport;

public sealed class FakeWprRunner : IWprRunner
{
    public const string TracesFolder = @"C:\Fake\data\traces";

    public bool IsAvailable { get; set; } = true;

    public WprResult Result { get; set; } = new(WprOutcome.Done);

    public Action? OnStart { get; set; }

    public List<string> Calls { get; } = [];

    public Task<WprResult> StartAsync(CancellationToken ct)
    {
        Calls.Add("start");
        OnStart?.Invoke();
        return Task.FromResult(Result);
    }

    public Task<WprResult> StopAsync(string fileName, CancellationToken ct)
    {
        Calls.Add("stop " + fileName);
        return Task.FromResult(Result.Outcome == WprOutcome.Done ? Result with { Path = Path.Combine(TracesFolder, fileName) } : Result);
    }

    public Task<WprResult> CancelAsync(CancellationToken ct)
    {
        Calls.Add("cancel");
        return Task.FromResult(Result);
    }
}
