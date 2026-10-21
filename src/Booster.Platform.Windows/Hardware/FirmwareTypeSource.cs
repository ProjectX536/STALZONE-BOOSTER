using System.ComponentModel;
using System.Runtime.InteropServices;
using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Windows.Win32;
using Windows.Win32.System.SystemInformation;

namespace Booster.Platform.Windows.Hardware;

public sealed class FirmwareTypeSource : IFirmwareTypeSource
{
    public const string Source = "GetFirmwareType";

    public unsafe Reading<FirmwareKind> Get()
    {
        // Функция есть с Windows 8 (метаданные Win32 пишут её как "windows8.0", а не NT 6.2), приложение
        // рассчитано на Windows 10/11; проверка нужна анализатору платформ.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
            return Reading<FirmwareKind>.Unsupported(Source, "requires Windows 10 or later");

        FIRMWARE_TYPE type;
        if (!PInvoke.GetFirmwareType(&type))
        {
            var error = new Win32Exception(Marshal.GetLastPInvokeError());
            return Reading<FirmwareKind>.Failure(Source, $"{nameof(Win32Exception)}: {error.Message}");
        }

        return Map((uint)type);
    }

    /// <param name="firmwareType">FIRMWARE_TYPE: 1 — BIOS, 2 — UEFI, 0 — неизвестно.</param>
    internal static Reading<FirmwareKind> Map(uint firmwareType) => firmwareType switch
    {
        (uint)FIRMWARE_TYPE.FirmwareTypeBios => Reading<FirmwareKind>.Known(FirmwareKind.LegacyBios, Source),
        (uint)FIRMWARE_TYPE.FirmwareTypeUefi => Reading<FirmwareKind>.Known(FirmwareKind.Uefi, Source),
        _ => Reading<FirmwareKind>.Unknown(Source, $"firmware type {firmwareType}"),
    };
}
