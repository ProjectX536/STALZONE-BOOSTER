using Booster.Core.Abstractions;

namespace Booster.Core.Tests.TestSupport;

/// <summary>Нагрузка системы, заданная тестом; считает замеры.</summary>
public sealed class FakeSystemLoad : ISystemLoadSource
{
    private int _samples;

    public int Samples => Volatile.Read(ref _samples);

    public SystemLoad Next { get; set; } = new(25, 40);

    public SystemLoad Sample()
    {
        Interlocked.Increment(ref _samples);
        return Next;
    }
}
