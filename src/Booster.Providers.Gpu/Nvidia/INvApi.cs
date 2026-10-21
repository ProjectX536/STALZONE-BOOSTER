using Booster.Core.Abstractions;
using Booster.Core.Inventory;

namespace Booster.Providers.Gpu.Nvidia;

internal readonly record struct NvResult<T>(int Status, T? Value)
{
    public bool Ok => Status == NvApiStatus.Ok;
}

internal sealed record NvPhysicalGpu(nint Handle, string Name, Reading<AdapterLuid> Luid, Reading<PciId> Pci);

internal sealed record NvDrsValue(uint SettingId, uint Value, bool IsCurrentPredefined, int Location);

internal sealed record NvDrsRead(string? ProfileName, IReadOnlyDictionary<uint, NvDrsValue> Values);

internal interface INvApi : IDisposable
{
    string DriverVersion { get; }

    NvResult<IReadOnlyList<NvPhysicalGpu>> EnumeratePhysicalGpus();

    NvResult<uint?> GetGraphicsUtilizationPercent(nint gpu);

    NvResult<int?> GetGpuTemperatureC(nint gpu);

    NvResult<(uint? GraphicsKhz, uint? MemoryKhz)> GetCurrentClocks(nint gpu);

    NvResult<uint> GetPerfDecreaseFlags(nint gpu);

    NvResult<uint> GetFanRpm(nint gpu);

    NvResult<NvDrsRead> ReadDrs(string? application, IReadOnlyList<uint> settingIds);
}

internal interface INvApiFactory
{
    INvApi? TryCreate(out int status);
}
