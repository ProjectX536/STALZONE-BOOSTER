using System.Globalization;

namespace Booster.Providers.Gpu.Nvidia;

internal static class NvApiStatus
{
    // nvapi_lite_common.h (NVIDIA/nvapi, R560): enum _NvAPI_Status.
    public const int Ok = 0;
    public const int Error = -1;
    public const int LibraryNotFound = -2;
    public const int NoImplementation = -3;
    public const int ApiNotInitialized = -4;
    public const int InvalidArgument = -5;
    public const int NvidiaDeviceNotFound = -6;
    public const int IncompatibleStructVersion = -9;
    public const int HandleInvalidated = -10;
    public const int ExpectedPhysicalGpuHandle = -101;
    public const int NotSupported = -104;
    public const int InvalidUserPrivilege = -137;
    public const int SettingNotFound = -160;
    public const int ProfileNotFound = -163;
    public const int ExecutableNotFound = -166;

    public static string Name(int status)
    {
        var name = status switch
        {
            Ok => "NVAPI_OK",
            Error => "NVAPI_ERROR",
            LibraryNotFound => "NVAPI_LIBRARY_NOT_FOUND",
            NoImplementation => "NVAPI_NO_IMPLEMENTATION",
            ApiNotInitialized => "NVAPI_API_NOT_INITIALIZED",
            InvalidArgument => "NVAPI_INVALID_ARGUMENT",
            NvidiaDeviceNotFound => "NVAPI_NVIDIA_DEVICE_NOT_FOUND",
            IncompatibleStructVersion => "NVAPI_INCOMPATIBLE_STRUCT_VERSION",
            HandleInvalidated => "NVAPI_HANDLE_INVALIDATED",
            ExpectedPhysicalGpuHandle => "NVAPI_EXPECTED_PHYSICAL_GPU_HANDLE",
            NotSupported => "NVAPI_NOT_SUPPORTED",
            InvalidUserPrivilege => "NVAPI_INVALID_USER_PRIVILEGE",
            SettingNotFound => "NVAPI_SETTING_NOT_FOUND",
            ProfileNotFound => "NVAPI_PROFILE_NOT_FOUND",
            ExecutableNotFound => "NVAPI_EXECUTABLE_NOT_FOUND",
            _ => null,
        };
        return name is null
            ? string.Create(CultureInfo.InvariantCulture, $"NVAPI status {status}")
            : string.Create(CultureInfo.InvariantCulture, $"{name} ({status})");
    }
}
