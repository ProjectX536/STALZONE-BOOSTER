namespace Booster.Providers.Gpu.Nvidia;

/// <summary>
/// Белый список функций NVAPI: адрес запрашивается через <c>nvapi_QueryInterface</c> только по этим идентификаторам.
/// Все функции — чтение. Функций записи (NvAPI_DRS_SetSetting, NvAPI_DRS_SaveSettings и любых других) здесь нет,
/// и тест NvApiFunctionIdsTests это проверяет.
/// </summary>
internal static class NvApiFunctionIds
{
    // nvapi_interface.h (NVIDIA/nvapi, R560): общие функции.
    public const uint Initialize = 0x0150E828;
    public const uint Unload = 0xD22BDD7E;
    public const uint SysGetDriverAndBranchVersion = 0x2926AAAD;

    // nvapi_interface.h: перечисление GPU и их идентификация.
    public const uint EnumPhysicalGpus = 0xE5AC921F;
    public const uint GpuGetFullName = 0xCEEE8E9F;
    public const uint GpuGetBusId = 0x1BE0B8E5;
    public const uint GpuGetPciIdentifiers = 0x2DDFB66E;
    public const uint GetLogicalGpuFromPhysicalGpu = 0xADD604D1;
    public const uint GpuGetLogicalGpuInfo = 0x842B066E;
    // Запасной путь получения LUID для драйверов без GPU_GetLogicalGpuInfo; с R520 объявлен устаревшим.
    public const uint GpuGetAdapterIdFromPhysicalGpu = 0x0FF07FDE;

    // nvapi_interface.h: датчики.
    public const uint GpuGetDynamicPstatesInfoEx = 0x60DED2ED;
    public const uint GpuGetThermalSettings = 0xE3640A56;
    public const uint GpuGetAllClockFrequencies = 0xDCB616C3;
    public const uint GpuGetPerfDecreaseInfo = 0x7F7F4600;
    public const uint GpuGetTachReading = 0x5F608315;

    // nvapi_interface.h: профили драйвера (DRS), только чтение; сессия всегда закрывается без сохранения.
    public const uint DrsCreateSession = 0x0694D52E;
    public const uint DrsLoadSettings = 0x375DBD6B;
    public const uint DrsGetCurrentGlobalProfile = 0x617BFF9F;
    public const uint DrsFindApplicationByName = 0xEEE566B2;
    public const uint DrsGetProfileInfo = 0x61CD6FD6;
    public const uint DrsGetSetting = 0x73BF8338;
    public const uint DrsDestroySession = 0xDAD9CFF8;
}
