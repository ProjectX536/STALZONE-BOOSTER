using System.ComponentModel;
using System.Runtime.Versioning;
using Booster.Core.Inventory;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Booster.Platform.Windows.Hardware;

public sealed class DisplayConfigSource : IDisplayConfigSource
{
    // Топология может смениться между двумя вызовами (подключили монитор).
    private const int MaxAttempts = 3;

    public IReadOnlyList<DisplayPathRecord> QueryActivePaths()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
            throw new PlatformNotSupportedException("QueryDisplayConfig is used on Windows 10 or later only");

        var (paths, modes) = Query();
        var result = new List<DisplayPathRecord>(paths.Length);
        for (var i = 0; i < paths.Length; i++)
        {
            var path = paths[i];
            var target = path.targetInfo;
            uint width = 0, height = 0;
            var primary = false;
            var sourceIndex = path.sourceInfo.modeInfoIdx;
            if (sourceIndex < modes.Length && modes[sourceIndex].infoType == DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
            {
                var mode = modes[sourceIndex].sourceMode;
                width = mode.width;
                height = mode.height;
                primary = mode.position.x == 0 && mode.position.y == 0;
            }

            var (hdrSupported, hdrEnabled) = AdvancedColor(target.adapterId, target.id);
            result.Add(new DisplayPathRecord(MonitorName(target.adapterId, target.id), width, height,
                target.refreshRate.Numerator, target.refreshRate.Denominator,
                new AdapterLuid(target.adapterId.LowPart, target.adapterId.HighPart), hdrSupported, hdrEnabled, primary));
        }

        return result;
    }

    [SupportedOSPlatform("windows10.0")]
    private static (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes) Query()
    {
        const QUERY_DISPLAY_CONFIG_FLAGS flags = QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS;
        for (var attempt = 1; ; attempt++)
        {
            var error = PInvoke.GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount);
            if (error != WIN32_ERROR.ERROR_SUCCESS)
                throw new Win32Exception((int)error, $"GetDisplayConfigBufferSizes failed: {error}");

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            error = PInvoke.QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes);
            if (error == WIN32_ERROR.ERROR_SUCCESS)
                return (paths[..(int)pathCount], modes[..(int)modeCount]);
            if (error != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER || attempt == MaxAttempts)
                throw new Win32Exception((int)error, $"QueryDisplayConfig failed: {error}");
        }
    }

    [SupportedOSPlatform("windows10.0")]
    private static unsafe string? MonitorName(LUID adapter, uint targetId)
    {
        var request = new DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = Header(DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME), adapter, targetId),
        };
        return PInvoke.DisplayConfigGetDeviceInfo(ref request.header) == 0 ? request.monitorFriendlyDeviceName.ToString() : null;
    }

    [SupportedOSPlatform("windows10.0")]
    private static unsafe (bool? Supported, bool? Enabled) AdvancedColor(LUID adapter, uint targetId)
    {
        var v2 = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
        {
            header = Header(DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2,
                sizeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2), adapter, targetId),
        };
        if (PInvoke.DisplayConfigGetDeviceInfo(ref v2.header) == 0)
            return (v2.highDynamicRangeSupported, v2.activeColorMode == DISPLAYCONFIG_ADVANCED_COLOR_MODE.DISPLAYCONFIG_ADVANCED_COLOR_MODE_HDR);

        var v1 = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
        {
            header = Header(DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                sizeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO), adapter, targetId),
        };
        if (PInvoke.DisplayConfigGetDeviceInfo(ref v1.header) == 0)
            return (v1.advancedColorSupported, v1.advancedColorEnabled && !v1.wideColorEnforced);

        return (null, null);
    }

    private static DISPLAYCONFIG_DEVICE_INFO_HEADER Header(DISPLAYCONFIG_DEVICE_INFO_TYPE type, int size, LUID adapter, uint targetId) =>
        new() { type = type, size = (uint)size, adapterId = adapter, id = targetId };
}
