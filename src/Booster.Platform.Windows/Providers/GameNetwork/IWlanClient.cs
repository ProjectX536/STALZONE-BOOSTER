namespace Booster.Platform.Windows.Providers.GameNetwork;

public enum WlanOpcode { InterfaceState, Rssi, ChannelNumber, BackgroundScanEnabled, MediaStreamingMode }

/// <summary>Шов для тестов над WLAN API; только запросы чтения. Методы возвращают код Win32.</summary>
public interface IWlanClient : IDisposable
{
    uint Open();

    /// <summary>Нет такого интерфейса — ERROR_NOT_FOUND.</summary>
    uint IsConnected(Guid interfaceId, out bool connected);

    uint Query(Guid interfaceId, WlanOpcode opcode, out int value);
}
