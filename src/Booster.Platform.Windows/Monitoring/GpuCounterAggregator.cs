using System.Text.RegularExpressions;
using Booster.Core.Inventory;

namespace Booster.Platform.Windows.Monitoring;

/// <summary>
/// Загрузка и занятая видеопамять адаптеров из экземпляров системных счётчиков. Загрузка движка — сумма по процессам,
/// загрузка адаптера — самый занятый тип движка (как в диспетчере задач), не больше 100.
/// </summary>
public static partial class GpuCounterAggregator
{
    private const double MaxPercent = 100;

    /// <param name="engines">(имя экземпляра "pid_…_luid_0x…_0x…_phys_0_eng_N_engtype_X", загрузка, %)</param>
    public static IReadOnlyDictionary<AdapterLuid, double> Utilization(IEnumerable<(string Instance, double Percent)> engines)
    {
        ArgumentNullException.ThrowIfNull(engines);
        var byEngineType = new Dictionary<(AdapterLuid Luid, string EngineType), double>();
        foreach (var (name, percent) in engines)
        {
            if (!GpuEngineInstanceName.TryParse(name, out var instance))
                continue;
            var key = (instance.Luid, instance.EngineType.ToUpperInvariant());
            byEngineType[key] = byEngineType.GetValueOrDefault(key) + percent;
        }

        var result = new Dictionary<AdapterLuid, double>();
        foreach (var ((luid, _), total) in byEngineType)
            result[luid] = Math.Max(result.GetValueOrDefault(luid), Math.Clamp(total, 0, MaxPercent));
        return result;
    }

    /// <param name="memory">(имя экземпляра "luid_0x…_0x…_phys_0", байты)</param>
    /// <remarks>Связанные физические адаптеры (phys_0, phys_1…) одного LUID суммируются.</remarks>
    public static IReadOnlyDictionary<AdapterLuid, long> DedicatedUsage(IEnumerable<(string Instance, long Bytes)> memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        var result = new Dictionary<AdapterLuid, long>();
        foreach (var (name, bytes) in memory)
        {
            if (TryParseAdapterInstance(name, out var luid))
                result[luid] = result.GetValueOrDefault(luid) + bytes;
        }
        return result;
    }

    private static bool TryParseAdapterInstance(string? name, out AdapterLuid luid)
    {
        luid = default;
        if (name is null)
            return false;
        var match = AdapterPattern().Match(name);
        return match.Success && AdapterLuid.TryParse(match.Groups["luid"].Value, out luid);
    }

    [GeneratedRegex(@"^luid_(?<luid>0x[0-9A-F]{8}_0x[0-9A-F]{8})(?:_phys_\d+)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AdapterPattern();
}
