using Booster.Core.Abstractions;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Hardware;

/// <summary>Файлы подкачки через WMI. Только чтение: настройки меняет пользователь сам.</summary>
public sealed class PagefileSource(ICimClient cim) : IPagefileSource
{
    private const string SystemQuery = "SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem";
    private const string SettingQuery = "SELECT Name, InitialSize, MaximumSize FROM Win32_PageFileSetting";
    private const string UsageQuery = "SELECT Name, AllocatedBaseSize, CurrentUsage, PeakUsage FROM Win32_PageFileUsage";

    public async Task<PagefileConfiguration> ReadAsync(CancellationToken ct)
    {
        CimRow? system = null;
        string? systemFailure = null;
        try
        {
            system = (await cim.QueryAsync(WmiNamespaces.CimV2, SystemQuery, ct).ConfigureAwait(false)).FirstOrDefault();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Флаг автоматического управления — отдельный вопрос; без него файлы всё равно читаются.
            systemFailure = $"{ex.GetType().Name}: {ex.Message}";
        }

        var settings = await cim.QueryAsync(WmiNamespaces.CimV2, SettingQuery, ct).ConfigureAwait(false);
        var usage = await cim.QueryAsync(WmiNamespaces.CimV2, UsageQuery, ct).ConfigureAwait(false);

        var configuration = PagefileMapper.Map(system, settings, usage);
        return systemFailure is null
            ? configuration
            : configuration with { AutomaticManaged = Reading<bool>.Failure(PagefileMapper.AutomaticSource, systemFailure) };
    }
}
