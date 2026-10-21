using System.Diagnostics;

namespace Booster.Platform.Windows.Monitoring;

public sealed record SelfUsageSample(double CpuPercent, long WorkingSetBytes);

public static class SelfUsageCalculator
{
    public static double CpuPercent(TimeSpan cpuDelta, TimeSpan wallDelta, int processorCount)
    {
        if (wallDelta <= TimeSpan.Zero || processorCount <= 0)
            return 0;

        var percent = cpuDelta.TotalSeconds * 100.0 / (wallDelta.TotalSeconds * processorCount);
        return Math.Clamp(percent, 0, 100);
    }
}

public sealed class SelfUsageMonitor : IDisposable
{
    private readonly TimeProvider _time;
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly Lock _gate = new();
    private TimeSpan _lastCpu;
    private long _lastTimestamp;

    public SelfUsageMonitor(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        _lastCpu = _process.TotalProcessorTime;
        _lastTimestamp = time.GetTimestamp();
    }

    public SelfUsageSample Sample()
    {
        lock (_gate)
        {
            _process.Refresh();
            var cpu = _process.TotalProcessorTime;
            var timestamp = _time.GetTimestamp();

            var percent = SelfUsageCalculator.CpuPercent(cpu - _lastCpu, _time.GetElapsedTime(_lastTimestamp, timestamp),
                Environment.ProcessorCount);

            _lastCpu = cpu;
            _lastTimestamp = timestamp;
            return new SelfUsageSample(percent, _process.WorkingSet64);
        }
    }

    public void Dispose() => _process.Dispose();
}
