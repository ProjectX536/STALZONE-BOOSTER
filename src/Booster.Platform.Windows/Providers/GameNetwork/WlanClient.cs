using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.WiFi;

namespace Booster.Platform.Windows.Providers.GameNetwork;

/// <summary>Клиент WLAN API только для чтения: состояние интерфейса и значения opcode размером 4 байта.</summary>
public sealed unsafe class WlanClient : IWlanClient
{
    // Версия 2 — WLAN API Windows Vista и новее.
    private const uint ClientVersion = 2;

    private WlanCloseHandleSafeHandle? _handle;

    public uint Open()
    {
        var error = PInvoke.WlanOpenHandle(ClientVersion, out _, out var handle);
        if (error != 0)
        {
            handle.Dispose();
            return error;
        }

        _handle?.Dispose();
        _handle = handle;
        return 0;
    }

    public uint IsConnected(Guid interfaceId, out bool connected)
    {
        connected = false;
        if (_handle is null)
            return (uint)WIN32_ERROR.ERROR_INVALID_HANDLE;

        var error = PInvoke.WlanEnumInterfaces(_handle, out var list);
        try
        {
            if (error != 0)
                return error;
            if (list is null)
                return (uint)WIN32_ERROR.ERROR_INVALID_DATA;

            foreach (ref readonly var info in list->InterfaceInfo.AsSpan((int)list->dwNumberOfItems))
            {
                if (info.InterfaceGuid == interfaceId)
                {
                    connected = info.isState == WLAN_INTERFACE_STATE.wlan_interface_state_connected;
                    return 0;
                }
            }

            return (uint)WIN32_ERROR.ERROR_NOT_FOUND;
        }
        finally
        {
            if (list is not null)
                PInvoke.WlanFreeMemory(list);
        }
    }

    public uint Query(Guid interfaceId, WlanOpcode opcode, out int value)
    {
        value = 0;
        if (_handle is null)
            return (uint)WIN32_ERROR.ERROR_INVALID_HANDLE;

        var error = PInvoke.WlanQueryInterface(_handle, interfaceId, Native(opcode), out var size, out var data);
        try
        {
            if (error != 0)
                return error;
            if (data is null || size < sizeof(int))
                return (uint)WIN32_ERROR.ERROR_INVALID_DATA;

            value = *(int*)data;
            return 0;
        }
        finally
        {
            if (data is not null)
                PInvoke.WlanFreeMemory(data);
        }
    }

    /// <summary>Закрывает дескриптор через WlanCloseHandle.</summary>
    public void Dispose() => _handle?.Dispose();

    // Все пять opcode возвращают 4 байта: WLAN_INTERFACE_STATE, LONG (RSSI, дБм), ULONG (канал), BOOL, BOOL.
    private static WLAN_INTF_OPCODE Native(WlanOpcode opcode) => opcode switch
    {
        WlanOpcode.InterfaceState => WLAN_INTF_OPCODE.wlan_intf_opcode_interface_state,
        WlanOpcode.Rssi => WLAN_INTF_OPCODE.wlan_intf_opcode_rssi,
        WlanOpcode.ChannelNumber => WLAN_INTF_OPCODE.wlan_intf_opcode_channel_number,
        WlanOpcode.BackgroundScanEnabled => WLAN_INTF_OPCODE.wlan_intf_opcode_background_scan_enabled,
        WlanOpcode.MediaStreamingMode => WLAN_INTF_OPCODE.wlan_intf_opcode_media_streaming_mode,
        _ => throw new ArgumentOutOfRangeException(nameof(opcode)),
    };
}
