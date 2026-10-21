namespace Booster.Platform.Windows.Hardware;

public interface ICpuTopologySource
{
    /// <summary>EfficiencyClass каждого физического ядра, по одному элементу на ядро всех сокетов.</summary>
    IReadOnlyList<byte> GetEfficiencyClassPerCore();
}
