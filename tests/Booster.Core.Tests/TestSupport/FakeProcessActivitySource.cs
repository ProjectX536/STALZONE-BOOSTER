using Booster.Core.Abstractions;

namespace Booster.Core.Tests.TestSupport;

/// <summary>Загрузка процессов из теста; вызов с номером <see cref="FailOnCall"/> бросает.</summary>
public sealed class FakeProcessActivitySource : IProcessActivitySource
{
    public static readonly ProcessActivity Empty = new(Reading<double>.Unknown(), Reading<TargetCpuUsage>.Unknown(), []);

    private int _count;

    public int FailOnCall { get; set; }

    /// <summary>Ответ по номеру вызова, с 1; по умолчанию — <see cref="Empty"/>.</summary>
    public Func<int, ProcessActivity>? Activity { get; set; }

    public (string? Name, int? ProcessId)? LastTarget { get; private set; }

    public int Calls => Volatile.Read(ref _count);

    public ProcessActivity Sample(string? processName, int? processId, int topCount)
    {
        var call = Interlocked.Increment(ref _count);
        LastTarget = (processName, processId);
        if (call == FailOnCall)
            throw new InvalidOperationException("process snapshot failed");
        return Activity?.Invoke(call) ?? Empty;
    }
}
