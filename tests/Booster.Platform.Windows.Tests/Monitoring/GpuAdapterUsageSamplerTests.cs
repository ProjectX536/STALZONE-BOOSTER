using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Booster.Core.Tests.TestSupport;
using Booster.Platform.Windows.Monitoring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Booster.Platform.Windows.Tests.Monitoring;

public sealed class GpuAdapterUsageSamplerTests
{
    private static readonly AdapterLuid Igpu = new(0xA, 0);
    private static readonly AdapterLuid Dgpu = new(0xB, 0);
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(2000); // FakeConfiguration: gpuEngineSampleIntervalMs

    private readonly FakeTimeProvider _time = new();
    private readonly FakeCounterSource _source = new();
    private readonly CapabilityRegistry _capabilities = new();
    private readonly ListLogger<GpuAdapterUsageSampler> _logger = new();

    private GpuAdapterUsageSampler CreateSampler() => new(_source, new FakeConfiguration(), _capabilities, _time, _logger);

    [Fact]
    public async Task Samples_every_interval_until_cancelled_and_returns_dominant_adapter()
    {
        // Первый опрос счётчиков всегда даёт нули: PerformanceCounter считает разность между двумя чтениями.
        _source.Results.Enqueue([new GpuEngineSample(Dgpu, "3D", 0)]);
        _source.Results.Enqueue([new GpuEngineSample(Igpu, "3D", 30), new GpuEngineSample(Dgpu, "3D", 70)]);
        _source.Results.Enqueue([new GpuEngineSample(Dgpu, "3D", 80)]);
        using var cts = new CancellationTokenSource();

        var sampling = CreateSampler().SampleUntilCancelledAsync(4242, cts.Token);
        await _source.WaitForCallsAsync(1);
        _time.Advance(Interval);
        await _source.WaitForCallsAsync(2);
        _time.Advance(Interval);
        await _source.WaitForCallsAsync(3);
        await cts.CancelAsync();
        var reading = await sampling;

        Assert.Equal(Dgpu, reading.Value);
        Assert.Equal(DataStatus.Available, reading.Status);
        Assert.All(_source.ProcessIds, pid => Assert.Equal(4242, pid));
        Assert.Equal(CapabilityStatus.Supported, _capabilities.Get(CapabilityIds.GpuEngineCounters).Status);
    }

    [Fact]
    public async Task No_sample_is_taken_before_the_interval_passes()
    {
        using var cts = new CancellationTokenSource();

        var sampling = CreateSampler().SampleUntilCancelledAsync(1, cts.Token);
        await _source.WaitForCallsAsync(1);
        _time.Advance(Interval - TimeSpan.FromMilliseconds(1));
        await cts.CancelAsync();
        await sampling;

        Assert.Single(_source.ProcessIds);
    }

    [Fact]
    public async Task Counter_failure_is_reported_not_thrown()
    {
        _source.Failure = new InvalidOperationException("Category does not exist.");

        var reading = await CreateSampler().SampleUntilCancelledAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(DataStatus.Failed, reading.Status);
        Assert.Contains("Category does not exist.", reading.Note, StringComparison.Ordinal);
        Assert.Equal(CapabilityStatus.Unknown, _capabilities.Get(CapabilityIds.GpuEngineCounters).Status);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    private sealed class FakeCounterSource : IGpuEngineCounterSource
    {
        private readonly List<int> _processIds = [];
        private readonly SemaphoreSlim _calls = new(0);

        public Queue<IReadOnlyList<GpuEngineSample>> Results { get; } = new();

        public Exception? Failure { get; set; }

        public IReadOnlyList<int> ProcessIds
        {
            get { lock (_processIds) return _processIds.ToArray(); }
        }

        public IReadOnlyList<GpuEngineSample> Sample(int processId)
        {
            try
            {
                lock (_processIds)
                {
                    _processIds.Add(processId);
                    if (Failure is not null)
                        throw Failure;
                    return Results.TryDequeue(out var result) ? result : [];
                }
            }
            finally
            {
                _calls.Release();
            }
        }

        public async Task WaitForCallsAsync(int count)
        {
            while (ProcessIds.Count < count)
                Assert.True(await _calls.WaitAsync(TimeSpan.FromSeconds(10)), $"Expected {count} samples.");
        }
    }
}
