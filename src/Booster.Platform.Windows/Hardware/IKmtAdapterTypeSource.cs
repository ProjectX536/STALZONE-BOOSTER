using Booster.Core.Inventory;

namespace Booster.Platform.Windows.Hardware;

/// <summary>Флаги <c>D3DKMT_ADAPTERTYPE</c>, которые выставляет графическое ядро Windows.</summary>
public sealed record KmtAdapterFlags(bool HybridDiscrete, bool HybridIntegrated, bool SoftwareDevice);

public interface IKmtAdapterTypeSource
{
    IReadOnlyDictionary<AdapterLuid, KmtAdapterFlags> Query();
}
