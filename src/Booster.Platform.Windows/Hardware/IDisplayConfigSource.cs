using Booster.Core.Inventory;

namespace Booster.Platform.Windows.Hardware;

/// <summary>
/// Активный путь вывода из QueryDisplayConfig. Частота — дробь, как её отдаёт Windows;
/// <c>null</c> в полях HDR — запрос DisplayConfigGetDeviceInfo для этого монитора не удался.
/// </summary>
public sealed record DisplayPathRecord(string? MonitorFriendlyName, uint Width, uint Height, uint RefreshNumerator,
    uint RefreshDenominator, AdapterLuid AdapterLuid, bool? AdvancedColorSupported, bool? AdvancedColorEnabled, bool IsPrimary);

public interface IDisplayConfigSource
{
    IReadOnlyList<DisplayPathRecord> QueryActivePaths();
}
