using System.Diagnostics;
using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Microsoft.Extensions.Logging;

namespace Booster.Platform.Windows.Monitoring;

public sealed partial class GpuSystemCounterSource(ILogger<GpuSystemCounterSource> logger) : IGpuSystemCounterSource
{
    public const string EngineCategory = "GPU Engine";
    public const string MemoryCategory = "GPU Adapter Memory";
    public const string UtilizationCounter = "Utilization Percentage";
    public const string DedicatedUsageCounter = "Dedicated Usage";
    public const string Source = "Performance counters GPU Engine, GPU Adapter Memory";
    public const string UtilizationSource = @"Performance counters GPU Engine\Utilization Percentage";
    public const string MemorySource = @"Performance counters GPU Adapter Memory\Dedicated Usage";

    private const string FirstSampleNote = "load needs a second reading of the counters";
    private const string NewEnginesNote = "engine instances appeared since the previous reading";

    private readonly Lock _lock = new();
    private Dictionary<string, CounterSample>? _previous;
    private Reading<bool>? _availability;
    private string? _loggedFailure;

    public Reading<bool> IsAvailable
    {
        get
        {
            lock (_lock)
                return _availability ??= Probe();
        }
    }

    public IReadOnlyDictionary<AdapterLuid, GpuCounterSample> Sample()
    {
        lock (_lock)
        {
            try
            {
                var engines = Read(EngineCategory, UtilizationCounter);
                var memory = Read(MemoryCategory, DedicatedUsageCounter);
                var previous = _previous;
                var measured = new List<(string Instance, double Percent)>();
                if (previous is not null)
                {
                    foreach (var (name, sample) in engines)
                    {
                        if (!previous.TryGetValue(name, out var before))
                            continue;
                        var percent = CounterSample.Calculate(before, sample);
                        if (float.IsFinite(percent))
                            measured.Add((name, percent));
                    }
                }

                var result = Combine(previous is not null, measured, engines.Keys,
                    GpuCounterAggregator.DedicatedUsage(memory.Select(m => (m.Key, m.Value.RawValue))));
                _previous = engines;
                _availability = Reading<bool>.Known(true, Source);
                _loggedFailure = null;
                return result;
            }
            catch (Exception ex)
            {
                _previous = null;
                _availability = Fail($"{ex.GetType().Name}: {ex.Message}");
                return new Dictionary<AdapterLuid, GpuCounterSample>();
            }
        }
    }

    internal static Dictionary<AdapterLuid, GpuCounterSample> Combine(bool hasBaseline,
        IReadOnlyList<(string Instance, double Percent)> measured, IEnumerable<string> engineInstances,
        IReadOnlyDictionary<AdapterLuid, long> dedicated)
    {
        var utilization = GpuCounterAggregator.Utilization(measured);
        var withEngines = new HashSet<AdapterLuid>();
        foreach (var name in engineInstances)
        {
            if (GpuEngineInstanceName.TryParse(name, out var instance))
                withEngines.Add(instance.Luid);
        }

        var result = new Dictionary<AdapterLuid, GpuCounterSample>();
        foreach (var luid in withEngines.Concat(dedicated.Keys).Concat(utilization.Keys).Distinct())
        {
            var load = !hasBaseline ? Reading<double>.Unknown(UtilizationSource, FirstSampleNote)
                : utilization.TryGetValue(luid, out var percent) ? Reading<double>.Known(percent, UtilizationSource)
                : withEngines.Contains(luid) ? Reading<double>.Unknown(UtilizationSource, NewEnginesNote)
                : Reading<double>.Known(0, UtilizationSource);
            var used = dedicated.TryGetValue(luid, out var bytes)
                ? Reading<long>.Known(bytes, MemorySource)
                : Reading<long>.Unknown(MemorySource);
            result[luid] = new GpuCounterSample(load, used);
        }
        return result;
    }

    private static Dictionary<string, CounterSample> Read(string category, string counter)
    {
        var instances = new PerformanceCounterCategory(category).ReadCategory()[counter]
            ?? throw new InvalidOperationException($"Counter '{counter}' is missing in category '{category}'.");
        var result = new Dictionary<string, CounterSample>(StringComparer.Ordinal);
        foreach (InstanceData instance in instances.Values)
            result[instance.InstanceName] = instance.Sample;
        return result;
    }

    private Reading<bool> Probe()
    {
        try
        {
            return PerformanceCounterCategory.Exists(EngineCategory) && PerformanceCounterCategory.Exists(MemoryCategory)
                ? Reading<bool>.Known(true, Source)
                : Fail("GPU performance counter categories are missing");
        }
        catch (Exception ex)
        {
            return Fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private Reading<bool> Fail(string reason)
    {
        if (reason != _loggedFailure)
        {
            _loggedFailure = reason;
            LogReadFailed(reason);
        }
        return Reading<bool>.Failure(Source, reason);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "GPU system counters are unavailable: {Reason}")]
    private partial void LogReadFailed(string reason);
}
