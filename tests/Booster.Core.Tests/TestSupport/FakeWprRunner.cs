using Booster.Core.Abstractions;

namespace Booster.Core.Tests.TestSupport;

/// <summary>wpr.exe из теста: запоминает вызовы и отвечает <see cref="Result"/>; успешная остановка возвращает путь в <see cref="TracesFolder"/>.</summary>
public sealed class FakeWprRunner : IWprRunner
{
    public const string TracesFolder = @"C:\Fake\data\traces";

    public bool IsAvailable { get; set; } = true;

    public WprResult Result { get; set; } = new(WprOutcome.Done);

    /// <summary>Вызывается в начале <see cref="StartAsync"/>: так тест видит состояние до запуска wpr.exe.</summary>
    public Action? OnStart { get; set; }

    /// <summary>"start", "stop имя.etl", "cancel".</summary>
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
