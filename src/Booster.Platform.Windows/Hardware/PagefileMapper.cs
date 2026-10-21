using Booster.Core.Abstractions;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Hardware;

public static class PagefileMapper
{
    public const string AutomaticSource = "WMI Win32_ComputerSystem.AutomaticManagedPagefile";

    public static PagefileConfiguration Map(CimRow? computerSystem, IReadOnlyList<CimRow> settings, IReadOnlyList<CimRow> usage)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(usage);

        var automatic = computerSystem?.GetBoolean("AutomaticManagedPagefile") is { } managed
            ? Reading<bool>.Known(managed, AutomaticSource)
            : Reading<bool>.Unknown(AutomaticSource);

        return new PagefileConfiguration(automatic,
            settings
                .Where(row => row.GetString("Name") is not null)
                .Select(row => new PagefileSetting(row.GetString("Name")!, row.GetInt32("InitialSize") ?? 0, row.GetInt32("MaximumSize") ?? 0))
                .ToArray(),
            usage
                .Where(row => row.GetString("Name") is not null)
                .Select(row => new PagefileUsage(row.GetString("Name")!, row.GetInt32("AllocatedBaseSize") ?? 0,
                    row.GetInt32("CurrentUsage") ?? 0, row.GetInt32("PeakUsage") ?? 0))
                .ToArray());
    }
}
