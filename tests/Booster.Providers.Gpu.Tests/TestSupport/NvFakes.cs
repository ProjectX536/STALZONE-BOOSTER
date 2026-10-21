using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Providers.Gpu.Nvidia;

namespace Booster.Providers.Gpu.Tests.TestSupport;

internal sealed class FakeNvApi : INvApi
{
    public string DriverVersion { get; set; } = "560.94";

    public List<NvPhysicalGpu> Gpus { get; } = [];

    /// <summary>Не null — каждый метод бросает это исключение.</summary>
    public Exception? Throw { get; set; }

    public int EnumerateStatus { get; set; }

    public Dictionary<nint, uint?> Utilization { get; } = [];
    public Dictionary<nint, int?> Temperature { get; } = [];
    public Dictionary<nint, (uint?, uint?)> Clocks { get; } = [];
    public Dictionary<nint, uint> PerfDecrease { get; } = [];
    public Dictionary<nint, uint> Fan { get; } = [];

    /// <summary>Код для всех датчиков; не Ok — значения не возвращаются.</summary>
    public int SensorStatus { get; set; }

    public int DrsStatus { get; set; }
    public string? ProfileName { get; set; } = "Base Profile";
    public Dictionary<uint, NvDrsValue> DrsValues { get; } = [];
    public List<string?> DrsApplications { get; } = [];

    public bool Disposed { get; private set; }

    public NvResult<IReadOnlyList<NvPhysicalGpu>> EnumeratePhysicalGpus() =>
        Call<IReadOnlyList<NvPhysicalGpu>>(EnumerateStatus, () => Gpus.ToList());

    public NvResult<uint?> GetGraphicsUtilizationPercent(nint gpu) => Sensor(() => Utilization.GetValueOrDefault(gpu, 50u));

    public NvResult<int?> GetGpuTemperatureC(nint gpu) => Sensor(() => Temperature.GetValueOrDefault(gpu, 60));

    public NvResult<(uint? GraphicsKhz, uint? MemoryKhz)> GetCurrentClocks(nint gpu) =>
        Sensor<(uint?, uint?)>(() => Clocks.GetValueOrDefault(gpu, (1_500_000u, 7_000_000u)));

    public NvResult<uint> GetPerfDecreaseFlags(nint gpu) => Sensor(() => PerfDecrease.GetValueOrDefault(gpu));

    public NvResult<uint> GetFanRpm(nint gpu) => Sensor(() => Fan.GetValueOrDefault(gpu, 1200u));

    public NvResult<NvDrsRead> ReadDrs(string? application, IReadOnlyList<uint> settingIds)
    {
        DrsApplications.Add(application);
        return Call(DrsStatus, () => new NvDrsRead(ProfileName,
            DrsValues.Where(v => settingIds.Contains(v.Key)).ToDictionary()));
    }

    public void Dispose() => Disposed = true;

    private NvResult<T> Sensor<T>(Func<T> value) => Call(SensorStatus, value);

    private NvResult<T> Call<T>(int status, Func<T> value)
    {
        if (Throw is not null)
            throw Throw;
        return status == NvApiStatus.Ok ? new(status, value()) : new(status, default);
    }
}

internal sealed class FakeNvApiFactory : INvApiFactory
{
    /// <summary>Каждый вызов TryCreate берёт следующий; очередь пуста — LIBRARY_NOT_FOUND.</summary>
    public Queue<FakeNvApi> Instances { get; } = [];

    public int Created { get; private set; }

    public INvApi? TryCreate(out int status)
    {
        Created++;
        if (Instances.TryDequeue(out var api))
        {
            status = NvApiStatus.Ok;
            return api;
        }
        status = NvApiStatus.LibraryNotFound;
        return null;
    }
}

internal static class NvFakes
{
    public static readonly PciId Pci = new(0x10DE, 0x2489, 0xC9721462, 0xA1);

    public static NvPhysicalGpu Gpu(nint handle, AdapterLuid? luid, PciId? pci = null) => new(handle, "NVIDIA Test",
        luid is { } l ? Reading<AdapterLuid>.Known(l, "nvapi") : Reading<AdapterLuid>.Unknown("nvapi", "luid failed"),
        pci is { } p ? Reading<PciId>.Known(p, "nvapi") : Reading<PciId>.Unknown("nvapi", "pci failed"));

    public static GpuAdapterInfo WithPci(this GpuAdapterInfo adapter, PciId pci) => adapter with { Pci = Reading<PciId>.Known(pci, GpuFakes.Source) };
}
