using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Hardware;

public sealed class CpuDetector(ICimClient cim, ICpuTopologySource topology) : IInventoryDetector<CpuInventory>
{
    private const string Query =
        "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor";

    public string SectionId => SectionIds.Cpu;

    public async Task<CpuInventory> DetectAsync(CancellationToken ct)
    {
        var rows = await cim.QueryAsync(WmiNamespaces.CimV2, Query, ct).ConfigureAwait(false);
        return new(CpuMapper.MapPackages(rows), ReadTopology());
    }

    private Reading<CoreTopology> ReadTopology()
    {
        try
        {
            return CpuMapper.MapTopology(topology.GetEfficiencyClassPerCore(), CpuTopologySource.Source);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Reading<CoreTopology>.Failure(CpuTopologySource.Source, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
