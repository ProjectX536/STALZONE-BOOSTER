using System.Diagnostics;
using System.Globalization;

namespace Booster.Platform.Windows.Monitoring;

public sealed class GpuEngineCounterSource : IGpuEngineCounterSource, IDisposable
{
    public const string Category = "GPU Engine";
    public const string Counter = "Utilization Percentage";

    private readonly Dictionary<string, PerformanceCounter> _counters = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private bool _disposed;

    public IReadOnlyList<GpuEngineSample> Sample(int processId)
    {
        var prefix = string.Create(CultureInfo.InvariantCulture, $"pid_{processId}_");
        var current = new PerformanceCounterCategory(Category).GetInstanceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.Ordinal);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            foreach (var stale in _counters.Keys.Where(name => !current.Contains(name)).ToArray())
                Drop(stale);

            var samples = new List<GpuEngineSample>();
            foreach (var name in current)
            {
                if (!GpuEngineInstanceName.TryParse(name, out var instance) || instance.ProcessId != processId)
                    continue;
                try
                {
                    if (_counters.TryGetValue(name, out var counter))
                    {
                        samples.Add(new GpuEngineSample(instance.Luid, instance.EngineType, counter.NextValue()));
                    }
                    else
                    {
                        counter = new PerformanceCounter(Category, Counter, name, readOnly: true);
                        _counters.Add(name, counter);
                        counter.NextValue();
                    }
                }
                catch (InvalidOperationException)
                {
                    Drop(name);
                }
            }
            return samples;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            foreach (var counter in _counters.Values)
                counter.Dispose();
            _counters.Clear();
            _disposed = true;
        }
    }

    private void Drop(string name)
    {
        if (_counters.Remove(name, out var counter))
            counter.Dispose();
    }
}
