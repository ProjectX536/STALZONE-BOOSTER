using Booster.Core.Inventory;

namespace Booster.Platform.Windows.Hardware;

public sealed record DisplayPathRecord(string? MonitorFriendlyName, uint Width, uint Height, uint RefreshNumerator,
    uint RefreshDenominator, AdapterLuid AdapterLuid, bool? AdvancedColorSupported, bool? AdvancedColorEnabled, bool IsPrimary);

public interface IDisplayConfigSource
{
    IReadOnlyList<DisplayPathRecord> QueryActivePaths();
}
