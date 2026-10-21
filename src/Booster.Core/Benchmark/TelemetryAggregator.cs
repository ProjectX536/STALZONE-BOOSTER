using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Inventory;
using Booster.Core.Sensors;

namespace Booster.Core.Benchmark;

public sealed record BackgroundProcess(string Name, double CpuPercent);

/// <summary>Сводка сигналов по замерам внутри окон прогонов (паузы не учитываются). Медианы — «длительно», пики — для отчёта.</summary>
/// <param name="Background">Посторонние процессы по убыванию медианной загрузки всех логических CPU.</param>
/// <param name="Throttle">null — внутри прогонов нет ни одного замера датчиков.</param>
/// <param name="DpcTimeMedianPercent">
/// Время DPC и прерываний — с Этапа 5.3, поэтому параметры необязательные: в файлах серий Этапа 4 их нет, и они читаются как Unknown.
/// </param>
public sealed record SeriesTelemetry(
    int SampleCount,
    Reading<string> RenderAdapterName,
    Reading<string> DriverVersion,
    Reading<double> GpuUtilizationPercent,
    Reading<double> VramUsedMedianPercent,
    Reading<double> VramUsedPeakPercent,
    Reading<double> GpuTemperaturePeakC,
    Reading<double> BusiestThreadPercent,
    Reading<double> TargetCpuPercent,
    Reading<double> SystemCpuPercent,
    Reading<double> AvailableMemoryMinPercent,
    Reading<double> CommitPeakPercent,
    Reading<double> PageReadsPerSecond,
    Reading<double> DiskBusyPercent,
    Reading<double> DiskLatencyMs,
    IReadOnlyList<BackgroundProcess> Background,
    ThrottleAssessment? Throttle,
    Reading<double> DpcTimeMedianPercent = default,
    Reading<double> DpcTimePeakPercent = default,
    Reading<double> InterruptTimeMedianPercent = default,
    Reading<double> InterruptTimePeakPercent = default);

/// <summary>
/// Сводка замеров серии. Не известное во всех замерах значение сохраняет самый частый статус и его причину:
/// «не поддерживается» не превращается в «неизвестно».
/// </summary>
public static class TelemetryAggregator
{
    public const string Source = "Benchmark telemetry";
    public const string NoSamplesNote = "no readings inside the runs";
    public const string SingleAdapterNote = "render adapter is unknown; the only hardware adapter is shown";
    public const string SeveralAdaptersNote = "render adapter is unknown and there are several hardware adapters";
    public const string AdapterNotSampledNote = "render adapter has no sensor readings";

    /// <summary>Адаптер — по LUID из захвата; LUID неизвестен и аппаратный адаптер один — он, с примечанием; иначе Unknown.</summary>
    public static SeriesTelemetry Aggregate(IReadOnlyList<BenchmarkSample> samples, IReadOnlyList<RunWindow> windows,
        Reading<AdapterLuid> renderAdapter, Thresholds thresholds, int backgroundCount)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentOutOfRangeException.ThrowIfNegative(backgroundCount);

        var inside = samples.Where(sample => windows.Any(window => window.Contains(sample.Timestamp))).ToList();
        var snapshots = inside.Select(sample => sample.Sensors).OfType<SensorSnapshot>().ToList();
        var activities = inside.Select(sample => sample.Activity).OfType<ProcessActivity>().ToList();
        var pressures = inside.Select(sample => sample.Pressure).OfType<SystemPressure>().ToList();

        var (adapter, adapterNote) = FindAdapter(snapshots, renderAdapter);
        var gpu = adapter is null
            ? []
            : snapshots.SelectMany(snapshot => snapshot.Gpus).Where(reading => SameAdapter(reading.Adapter, adapter)).Select(reading => reading.Telemetry).ToList();
        var vram = gpu.Select(telemetry => VramPercent(telemetry, adapter!)).ToList();

        return new SeriesTelemetry(
            inside.Count,
            adapter is null ? Reading<string>.Unknown(Source, adapterNote)
                : Reading<string>.Known(adapter.Name, renderAdapter.Source ?? Source) with { Note = adapterNote },
            adapter?.DriverVersion ?? Reading<string>.Unknown(Source, adapterNote),
            GpuSummary(adapter, adapterNote, gpu.Select(t => t.UtilizationPercent), MedianOf),
            GpuSummary(adapter, adapterNote, vram, MedianOf),
            GpuSummary(adapter, adapterNote, vram, values => values.Max()),
            GpuSummary(adapter, adapterNote, gpu.Select(t => t.TemperatureC), values => values.Max()),
            Summarize(activities.Select(a => Project(a.Target, target => target.BusiestThreadPercent)), MedianOf),
            Summarize(activities.Select(a => Project(a.Target, target => target.CpuPercent)), MedianOf),
            Summarize(activities.Select(a => a.SystemCpuPercent), MedianOf),
            Summarize(pressures.Select(p => p.AvailableMemoryPercent), values => values.Min()),
            Summarize(pressures.Select(p => p.CommitPercent), values => values.Max()),
            Summarize(pressures.Select(p => p.PageReadsPerSecond), MedianOf),
            Summarize(pressures.Select(p => p.DiskBusyPercent), MedianOf),
            Summarize(pressures.Select(p => p.DiskLatencyMs), MedianOf),
            Background(activities, backgroundCount),
            snapshots.Count == 0 ? null : ThermalThrottleDetector.Assess(snapshots, thresholds.Throttle),
            Summarize(pressures.Select(p => p.DpcTimePercent), MedianOf),
            Summarize(pressures.Select(p => p.DpcTimePercent), values => values.Max()),
            Summarize(pressures.Select(p => p.InterruptTimePercent), MedianOf),
            Summarize(pressures.Select(p => p.InterruptTimePercent), values => values.Max()));
    }

    private static (GpuAdapterInfo? Adapter, string? Note) FindAdapter(IReadOnlyList<SensorSnapshot> snapshots, Reading<AdapterLuid> renderAdapter)
    {
        var adapters = snapshots.SelectMany(snapshot => snapshot.Gpus).Select(reading => reading.Adapter).ToList();
        if (renderAdapter.IsAvailable)
        {
            var match = adapters.FirstOrDefault(adapter => adapter.Luid.IsAvailable && adapter.Luid.Value == renderAdapter.Value);
            return match is null ? (null, snapshots.Count == 0 ? NoSamplesNote : AdapterNotSampledNote) : (match, null);
        }

        var hardware = new List<GpuAdapterInfo>();
        foreach (var adapter in adapters.Where(adapter => !adapter.IsSoftware))
        {
            if (!hardware.Any(known => SameAdapter(known, adapter)))
                hardware.Add(adapter);
        }
        return hardware.Count switch
        {
            1 => (hardware[0], SingleAdapterNote),
            0 => (null, snapshots.Count == 0 ? NoSamplesNote : AdapterNotSampledNote),
            _ => (null, SeveralAdaptersNote),
        };
    }

    private static bool SameAdapter(GpuAdapterInfo left, GpuAdapterInfo right) =>
        left.Luid.IsAvailable && right.Luid.IsAvailable
            ? left.Luid.Value == right.Luid.Value
            : string.Equals(left.Name, right.Name, StringComparison.Ordinal);

    private static Reading<double> VramPercent(GpuTelemetry telemetry, GpuAdapterInfo adapter)
    {
        var used = telemetry.VramUsedBytes;
        if (!used.IsAvailable)
            return new Reading<double>(default, used.Status, used.Source, used.Note);
        var total = telemetry.VramTotalBytes is { IsAvailable: true, Value: > 0 } ? telemetry.VramTotalBytes : adapter.DedicatedVideoMemoryBytes;
        if (!total.IsAvailable || total.Value <= 0)
            return Reading<double>.Unknown(total.Source, total.Note);
        return Reading<double>.Known(Math.Clamp(used.Value * 100.0 / total.Value, 0, 100), used.Source ?? Source);
    }

    private static Reading<double> GpuSummary(GpuAdapterInfo? adapter, string? adapterNote, IEnumerable<Reading<double>> readings,
        Func<double[], double> summary) =>
        adapter is null ? Reading<double>.Unknown(Source, adapterNote) : Summarize(readings, summary);

    private static Reading<double> Project<T>(Reading<T> reading, Func<T, double> value) =>
        reading.IsAvailable ? Reading<double>.Known(value(reading.Value!), reading.Source ?? Source)
            : new Reading<double>(default, reading.Status, reading.Source, reading.Note);

    private static Reading<double> Summarize(IEnumerable<Reading<double>> readings, Func<double[], double> summary)
    {
        var all = readings.ToList();
        var known = all.Where(reading => reading.IsAvailable).ToList();
        if (known.Count > 0)
            return Reading<double>.Known(summary([.. known.Select(reading => reading.Value)]), known[0].Source ?? Source);
        if (all.Count == 0)
            return Reading<double>.Unknown(Source, NoSamplesNote);
        return all.GroupBy(reading => reading.Status).MaxBy(group => group.Count())!.Last();
    }

    /// <summary>Медиана по замерам с действующим снимком; процесс, которого нет в замере, — 0 в этом замере.</summary>
    private static IReadOnlyList<BackgroundProcess> Background(IReadOnlyList<ProcessActivity> activities, int count)
    {
        var perSample = activities
            .Where(activity => activity.SystemCpuPercent.IsAvailable)
            .Select(activity => activity.TopOthers
                .GroupBy(usage => usage.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Sum(usage => usage.CpuPercent), StringComparer.OrdinalIgnoreCase))
            .ToList();

        return perSample
            .SelectMany(sample => sample.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => new BackgroundProcess(name, MedianOf([.. perSample.Select(sample => sample.GetValueOrDefault(name))])))
            .Where(process => process.CpuPercent > 0)
            .OrderByDescending(process => process.CpuPercent)
            .ThenBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
            .Take(count)
            .ToArray();
    }

    private static double MedianOf(double[] values) => Median.Of(values);
}
