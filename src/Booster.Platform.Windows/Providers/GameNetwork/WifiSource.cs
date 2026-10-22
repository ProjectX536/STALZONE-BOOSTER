using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.GameNetwork;
using Microsoft.Extensions.Logging;
using Windows.Win32.Foundation;

namespace Booster.Platform.Windows.Providers.GameNetwork;

/// <summary>Сигнал, канал, фоновый поиск и режим стриминга подключённого Wi-Fi-интерфейса. Только чтение.</summary>
public sealed partial class WifiSource(Func<IWlanClient> clients, ILogger<WifiSource> logger) : IWifiSource
{
    public const string Source = "WLAN API";
    public const string AccessDeniedNote = "access denied: location consent";

    public Reading<WifiStatus> Read(string adapterId)
    {
        if (!Guid.TryParse(adapterId, out var interfaceId))
            return Reading<WifiStatus>.Unknown(Source, "adapter id is not a GUID");

        try
        {
            using var client = clients();
            return Read(client, interfaceId);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            LogApiUnavailable(logger, ex.GetType().Name);
            return Reading<WifiStatus>.Unsupported(Source, "wlanapi.dll is unavailable");
        }
        catch (Exception ex)
        {
            LogReadFailed(logger, ex.GetType().Name, ex.Message);
            return Reading<WifiStatus>.Failure(Source, ex.GetType().Name);
        }
    }

    private Reading<WifiStatus> Read(IWlanClient client, Guid interfaceId)
    {
        var error = client.Open();
        if (error == (uint)WIN32_ERROR.ERROR_SERVICE_NOT_ACTIVE)
            return Reading<WifiStatus>.Unsupported(Source, "WLAN AutoConfig service is not running");
        if (error != 0)
        {
            LogOpenFailed(logger, error);
            return Reading<WifiStatus>.Failure(Source, Describe("WlanOpenHandle", error));
        }

        error = client.IsConnected(interfaceId, out var connected);
        if (error == (uint)WIN32_ERROR.ERROR_NOT_FOUND)
            return Reading<WifiStatus>.Unknown(Source, "not a WLAN interface");
        if (error != 0)
        {
            LogEnumFailed(logger, error);
            return Reading<WifiStatus>.Failure(Source, Describe("WlanEnumInterfaces", error));
        }

        if (!connected)
            return Reading<WifiStatus>.Unknown(Source, "WLAN interface is not connected");

        var channel = Query(client, interfaceId, WlanOpcode.ChannelNumber, v => v);
        var band = channel.IsAvailable ? WifiBandResolver.FromChannel(channel.Value) : Reading<WifiBand>.Unknown(Source, "channel is unknown");
        return Reading<WifiStatus>.Known(new WifiStatus(
            Query(client, interfaceId, WlanOpcode.Rssi, v => v),
            channel,
            band,
            Query(client, interfaceId, WlanOpcode.BackgroundScanEnabled, IsTrue),
            Query(client, interfaceId, WlanOpcode.MediaStreamingMode, IsTrue)), Source);
    }

    private static Reading<T> Query<T>(IWlanClient client, Guid interfaceId, WlanOpcode opcode, Func<int, T> convert)
    {
        var error = client.Query(interfaceId, opcode, out var value);
        return error switch
        {
            0 => Reading<T>.Known(convert(value), Source),
            (uint)WIN32_ERROR.ERROR_ACCESS_DENIED => Reading<T>.Unknown(Source, AccessDeniedNote),
            _ => Reading<T>.Unknown(Source, Describe("WLAN query", error)),
        };
    }

    // Win32 BOOL: истина — любое ненулевое значение.
    private static bool IsTrue(int value) => value != 0;

    private static string Describe(string function, uint win32Error) =>
        string.Create(CultureInfo.InvariantCulture, $"{function} error {win32Error}");

    [LoggerMessage(Level = LogLevel.Information, Message = "Wi-Fi: WLAN API is unavailable ({ExceptionType})")]
    private static partial void LogApiUnavailable(ILogger logger, string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Wi-Fi: WlanOpenHandle failed with error {Error}")]
    private static partial void LogOpenFailed(ILogger logger, uint error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Wi-Fi: WlanEnumInterfaces failed with error {Error}")]
    private static partial void LogEnumFailed(ILogger logger, uint error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Wi-Fi: read failed with {ExceptionType}: {Message}")]
    private static partial void LogReadFailed(ILogger logger, string exceptionType, string message);
}
