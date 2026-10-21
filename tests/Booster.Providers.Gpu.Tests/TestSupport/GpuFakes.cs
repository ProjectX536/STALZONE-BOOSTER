using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Hardware;
using Booster.Platform.Windows.Monitoring;

namespace Booster.Providers.Gpu.Tests.TestSupport;

public static class GpuFakes
{
    public const string Source = "test";
    public const string NotReported = "test: not reported";

    public static readonly AdapterLuid Luid = new(0xCA31, 0);
    public static readonly AdapterLuid OtherLuid = new(0xBEEF, 0);

    /// <summary>luid null — LUID неизвестен.</summary>
    public static GpuAdapterInfo Adapter(AdapterLuid? luid, GpuVendor vendor = GpuVendor.Nvidia, long? vramTotal = null,
        bool isSoftware = false) =>
        new("Test GPU", vendor, Reading<PciId>.Unknown(),
            luid is { } l ? Reading<AdapterLuid>.Known(l, Source) : Reading<AdapterLuid>.Unknown(Source),
            Reading<GpuAdapterKind>.Known(GpuAdapterKind.Discrete, Source),
            vramTotal is { } v ? Reading<long>.Known(v, Source) : Reading<long>.Unknown(Source),
            Reading<long>.Unknown(), Reading<string>.Unknown(), Reading<DateOnly>.Unknown(), Reading<int>.Unknown(), isSoftware, null);

    /// <summary>null — значение Unknown с примечанием <see cref="NotReported"/>.</summary>
    public static GpuCounterSample Counters(double? utilization, long? usedBytes) => new(Value(utilization), Value(usedBytes));

    /// <summary>null — значение Unknown с примечанием <see cref="NotReported"/>.</summary>
    public static KmtPerfData Kmt(double? temperature = 65.3, double? limit = 83, int? fan = 1450, double? memoryClock = 7001) =>
        new(Value(temperature), Value(limit), Value(fan), Value(memoryClock));

    public static Reading<T> Value<T>(T? value) where T : struct =>
        value is { } v ? Reading<T>.Known(v, Source) : Reading<T>.Unknown(Source, NotReported);
}

public sealed class FakeGpuCounterSource : IGpuSystemCounterSource
{
    public Dictionary<AdapterLuid, GpuCounterSample> Next { get; set; } = [];

    public int Samples { get; private set; }

    public Reading<bool> IsAvailable => Reading<bool>.Known(true, GpuFakes.Source);

    public IReadOnlyDictionary<AdapterLuid, GpuCounterSample> Sample()
    {
        Samples++;
        return new Dictionary<AdapterLuid, GpuCounterSample>(Next);
    }
}

public sealed class FakeKmtPerfDataSource : IKmtPerfDataSource
{
    public Dictionary<AdapterLuid, KmtPerfData> Data { get; } = [];

    public List<AdapterLuid> Reads { get; } = [];

    public KmtPerfData Read(AdapterLuid luid)
    {
        Reads.Add(luid);
        return Data.TryGetValue(luid, out var data) ? data : GpuFakes.Kmt(null, null, null, null);
    }
}
