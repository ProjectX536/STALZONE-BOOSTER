using Booster.Core.Abstractions;

namespace Booster.Core.Tests.TestSupport;

/// <summary>Давление памяти и диска из теста; вызов с номером <see cref="FailOnCall"/> бросает.</summary>
public sealed class FakeSystemPressureSource : ISystemPressureSource
{
    private int _count;

    public int FailOnCall { get; set; }

    /// <summary>Ответ по номеру вызова, с 1; по умолчанию — <see cref="Pressure"/> с нулями.</summary>
    public Func<int, SystemPressure>? Answer { get; set; }

    public int Calls => Volatile.Read(ref _count);

    public SystemPressure Sample()
    {
        var call = Interlocked.Increment(ref _count);
        if (call == FailOnCall)
            throw new InvalidOperationException("pressure read failed");
        return Answer?.Invoke(call) ?? Pressure(50, 40, 0, 0, 0);
    }

    public static SystemPressure Pressure(double availableMemory, double commit, double pageReads, double diskBusy, double diskLatencyMs,
        double dpcTime = 0, double interruptTime = 0) =>
        new(Known(availableMemory), Known(commit), Known(pageReads), Known(diskBusy), Known(diskLatencyMs), Known(dpcTime), Known(interruptTime));

    private static Reading<double> Known(double value) => Reading<double>.Known(value, "test-pressure");
}
