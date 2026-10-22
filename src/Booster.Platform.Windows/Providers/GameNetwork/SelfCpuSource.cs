using Booster.Core.Abstractions;
using Booster.Platform.Windows.Monitoring;

namespace Booster.Platform.Windows.Providers.GameNetwork;

/// <summary>Свой экземпляр монитора: отсчёт не сбивается опросом строки состояния.</summary>
public sealed class SelfCpuSource(TimeProvider time) : ISelfCpuSource, IDisposable
{
    private readonly SelfUsageMonitor _monitor = new(time);

    public double SampleCpuPercent() => _monitor.Sample().CpuPercent;

    public void Dispose() => _monitor.Dispose();
}
