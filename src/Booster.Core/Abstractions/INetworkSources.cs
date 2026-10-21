namespace Booster.Core.Abstractions;

public enum NetworkMedium
{
    Ethernet,
    Wireless,
    Other,
}

/// <summary>Сетевой адаптер.</summary>
/// <param name="LinkSpeedBitsPerSecond">Скорость соединения; null — адаптер её не сообщает.</param>
/// <param name="Gateways">IPv4 сначала, без 0.0.0.0.</param>
public sealed record NetworkAdapterInfo(string Id, string Name, string Description, NetworkMedium Medium, long? LinkSpeedBitsPerSecond,
    IReadOnlyList<string> Gateways);

/// <summary>Сетевые адаптеры и их счётчики. Только чтение.</summary>
public interface INetworkAdapterSource
{
    /// <summary>Только включённые адаптеры, без loopback и туннелей.</summary>
    IReadOnlyList<NetworkAdapterInfo> Read();

    /// <summary>Принято + отправлено байт с запуска адаптера.</summary>
    Reading<long> ReadTrafficBytes(string adapterId);
}

/// <summary>Эхо-запрос ICMP.</summary>
public interface IPingSource
{
    /// <summary>Время ответа, мс; null — нет ответа за timeout. Имя не разрешилось — PingFailedException.</summary>
    Task<double?> PingAsync(string host, TimeSpan timeout, CancellationToken ct);
}

/// <summary>Запрос к узлу невозможен (например, имя не разрешилось) — повторять бессмысленно.</summary>
public sealed class PingFailedException(string message, Exception? inner = null) : Exception(message, inner);
