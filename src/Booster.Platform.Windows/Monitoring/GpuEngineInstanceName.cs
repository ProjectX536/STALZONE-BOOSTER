using System.Globalization;
using System.Text.RegularExpressions;
using Booster.Core.Inventory;

namespace Booster.Platform.Windows.Monitoring;

public readonly record struct GpuEngineInstance(int ProcessId, AdapterLuid Luid, string EngineType);

/// <summary>
/// Имя экземпляра счётчика «GPU Engine»: <c>pid_1234_luid_0x00000000_0x0000C2A1_phys_0_eng_0_engtype_3D</c>.
/// LUID в нём — тот же, что у DXGI, поэтому по нему адаптер сопоставляется с инвентаризацией.
/// </summary>
public static partial class GpuEngineInstanceName
{
    public static bool TryParse(string? instanceName, out GpuEngineInstance instance)
    {
        instance = default;
        if (instanceName is null)
            return false;

        var match = Pattern().Match(instanceName);
        if (!match.Success
            || !int.TryParse(match.Groups["pid"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var processId)
            || !AdapterLuid.TryParse(match.Groups["luid"].Value, out var luid))
            return false;

        instance = new GpuEngineInstance(processId, luid, match.Groups["type"].Value);
        return true;
    }

    [GeneratedRegex(@"^pid_(?<pid>\d+)_luid_(?<luid>0x[0-9A-F]{8}_0x[0-9A-F]{8})(?:_phys_\d+)?(?:_eng_\d+)?_engtype_(?<type>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
