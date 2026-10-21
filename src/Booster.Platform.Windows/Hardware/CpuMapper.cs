using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Hardware;

public static class CpuMapper
{
    private const string Wmi = "WMI Win32_Processor";

    /// <summary>Строка на сокет. Ноль ядер или частоты — не значение, а его отсутствие.</summary>
    public static IReadOnlyList<CpuPackage> MapPackages(IReadOnlyList<CimRow> win32Processor)
    {
        ArgumentNullException.ThrowIfNull(win32Processor);
        return win32Processor.Select(MapPackage).ToArray();
    }

    /// <summary>
    /// Ядра с наибольшим EfficiencyClass — производительные, остальные — энергоэффективные.
    /// Один класс на все ядра — обычный процессор без гибридной архитектуры.
    /// </summary>
    public static Reading<CoreTopology> MapTopology(IReadOnlyList<byte> efficiencyClasses, string source)
    {
        ArgumentNullException.ThrowIfNull(efficiencyClasses);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (efficiencyClasses.Count == 0)
            return Reading<CoreTopology>.Unknown(source, "no processor cores reported");

        var top = efficiencyClasses.Max();
        var performance = efficiencyClasses.Count(c => c == top);
        var efficiency = efficiencyClasses.Count - performance;
        return Reading<CoreTopology>.Known(new(performance, efficiency, efficiency > 0), source);
    }

    private static CpuPackage MapPackage(CimRow row) => new(
        // Имя пустое, только если WMI его не сообщил: показывать вместо него выдуманное нельзя.
        OemPlaceholders.Clean(row.GetString("Name")) ?? string.Empty,
        Text(row, "Manufacturer"),
        Positive(row, "NumberOfCores"),
        Positive(row, "NumberOfLogicalProcessors"),
        Positive(row, "MaxClockSpeed"));

    private static Reading<string> Text(CimRow row, string property) =>
        OemPlaceholders.Clean(row.GetString(property)) is { } value
            ? Reading<string>.Known(value, $"{Wmi}.{property}")
            : Reading<string>.Unknown($"{Wmi}.{property}");

    private static Reading<int> Positive(CimRow row, string property) =>
        row.GetInt32(property) is > 0 and var value
            ? Reading<int>.Known(value, $"{Wmi}.{property}")
            : Reading<int>.Unknown($"{Wmi}.{property}");
}
