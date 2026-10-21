namespace Booster.Platform.Windows.Hardware;

public interface ICpuTopologySource
{
    IReadOnlyList<byte> GetEfficiencyClassPerCore();
}
