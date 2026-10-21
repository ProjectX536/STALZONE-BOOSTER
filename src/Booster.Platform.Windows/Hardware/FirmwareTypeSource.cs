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

    internal static Reading<FirmwareKind> Map(uint firmwareType) => firmwareType switch
    {
        (uint)FIRMWARE_TYPE.FirmwareTypeBios => Reading<FirmwareKind>.Known(FirmwareKind.LegacyBios, Source),
        (uint)FIRMWARE_TYPE.FirmwareTypeUefi => Reading<FirmwareKind>.Known(FirmwareKind.Uefi, Source),
        _ => Reading<FirmwareKind>.Unknown(Source, $"firmware type {firmwareType}"),
    };
}
