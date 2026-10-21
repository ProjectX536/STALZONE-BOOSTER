using Booster.Core.Abstractions;
using Booster.Core.Inventory;

namespace Booster.Providers.Gpu.Nvidia;

/// <summary>Результат вызова NVAPI: код <see cref="NvApiStatus"/> и значение, если вызов успешен.</summary>
internal readonly record struct NvResult<T>(int Status, T? Value)
{
    public bool Ok => Status == NvApiStatus.Ok;
}

/// <param name="Name">Имя из NvAPI_GPU_GetFullName; пусто, если драйвер его не сообщил.</param>
internal sealed record NvPhysicalGpu(nint Handle, string Name, Reading<AdapterLuid> Luid, Reading<PciId> Pci);

/// <param name="Location">NVDRS_SETTING_LOCATION: 0 — профиль, из которого читали, 1 — глобальный, 2 — базовый, 3 — значение по умолчанию.</param>
internal sealed record NvDrsValue(uint SettingId, uint Value, bool IsCurrentPredefined, int Location);

/// <summary>Values: только найденные параметры типа DWORD; SETTING_NOT_FOUND — ключа нет. ProfileName null — профиль приложения не найден.</summary>
internal sealed record NvDrsRead(string? ProfileName, IReadOnlyDictionary<uint, NvDrsValue> Values);

/// <summary>NVAPI только для чтения. Методы не бросают исключений: ошибка — код в <see cref="NvResult{T}.Status"/>.</summary>
internal interface INvApi : IDisposable
{
    /// <summary>"560.94" из NvAPI_SYS_GetDriverAndBranchVersion; пусто, если драйвер версию не сообщил.</summary>
    string DriverVersion { get; }

    NvResult<IReadOnlyList<NvPhysicalGpu>> EnumeratePhysicalGpus();

    /// <summary>null — домен GPU не present.</summary>
    NvResult<uint?> GetGraphicsUtilizationPercent(nint gpu);

    /// <summary>null — нет датчика с target GPU.</summary>
    NvResult<int?> GetGpuTemperatureC(nint gpu);

    /// <summary>Текущие частоты в кГц; null — домен не present.</summary>
    NvResult<(uint? GraphicsKhz, uint? MemoryKhz)> GetCurrentClocks(nint gpu);

    /// <summary>Флаги <see cref="NvPerfDecrease"/>.</summary>
    NvResult<uint> GetPerfDecreaseFlags(nint gpu);

    NvResult<uint> GetFanRpm(nint gpu);

    /// <summary>Читает параметры глобального профиля (<paramref name="application"/> = null) или профиля приложения.</summary>
    NvResult<NvDrsRead> ReadDrs(string? application, IReadOnlyList<uint> settingIds);
}

internal interface INvApiFactory
{
    /// <summary>null — нет nvapi64.dll или Initialize вернула ошибку; status — код NVAPI или LIBRARY_NOT_FOUND.</summary>
    INvApi? TryCreate(out int status);
}
