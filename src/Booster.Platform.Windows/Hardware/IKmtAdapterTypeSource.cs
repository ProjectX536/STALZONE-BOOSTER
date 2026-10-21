using Booster.Core.Inventory;

namespace Booster.Platform.Windows.Hardware;

public sealed record KmtAdapterFlags(bool HybridDiscrete, bool HybridIntegrated, bool SoftwareDevice);

public interface IKmtAdapterTypeSource
{
    IReadOnlyDictionary<AdapterLuid, KmtAdapterFlags> Query();
}
