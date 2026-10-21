using Booster.Core.Abstractions;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Hardware;

/// <summary>Строки Win32_Service и Win32_DependentService → <see cref="ServiceInfo"/>.</summary>
public static class ServiceInventoryMapper
{
    private const string ServiceReference = "Win32_Service.Name=\"";

    /// <param name="serviceDll">ServiceDll по имени службы; вызывается только для служб svchost.</param>
    /// <param name="publisher">Издатель по пути исполняемого файла.</param>
    public static IReadOnlyList<ServiceInfo> Map(IReadOnlyList<CimRow> services, IReadOnlyList<CimRow> dependencies,
        Func<string, string?> serviceDll, Func<string?, Reading<string>> publisher)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(serviceDll);
        ArgumentNullException.ThrowIfNull(publisher);

        var dependents = Dependents(dependencies);
        var result = new List<ServiceInfo>(services.Count);
        foreach (var row in services)
        {
            if (row.GetString("Name") is not { } name)
                continue;

            var pathName = row.GetString("PathName");
            var image = ServiceImagePath.Resolve(pathName, ServiceImagePath.IsSharedHost(pathName) ? serviceDll(name) : null);
            var processId = row.GetInt32("ProcessId");
            result.Add(new ServiceInfo(
                name,
                row.GetString("DisplayName") ?? name,
                State(row.GetString("State")),
                StartMode(row.GetString("StartMode")),
                row.GetBoolean("DelayedAutoStart") ?? false,
                image,
                processId is > 0 ? processId : null,
                publisher(image),
                dependents.TryGetValue(name, out var list) ? list : []));
        }
        return result;
    }

    private static Dictionary<string, List<string>> Dependents(IReadOnlyList<CimRow> dependencies)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in dependencies)
        {
            // Ссылки на драйверы (Win32_SystemDriver) пропускаются: здесь только службы.
            if (ServiceName(row.GetString("Antecedent")) is not { } antecedent || ServiceName(row.GetString("Dependent")) is not { } dependent)
                continue;

            if (!result.TryGetValue(antecedent, out var list))
                result[antecedent] = list = [];
            if (!list.Contains(dependent, StringComparer.OrdinalIgnoreCase))
                list.Add(dependent);
        }
        return result;
    }

    /// <summary><c>\\HOST\root\cimv2:Win32_Service.Name="RpcSs"</c> → <c>RpcSs</c>.</summary>
    private static string? ServiceName(string? reference)
    {
        var start = reference?.IndexOf(ServiceReference, StringComparison.OrdinalIgnoreCase) ?? -1;
        if (start < 0)
            return null;

        start += ServiceReference.Length;
        var end = reference!.IndexOf('"', start);
        return end > start ? reference[start..end] : null;
    }

    private static ServiceRunState State(string? state) => state switch
    {
        "Running" => ServiceRunState.Running,
        "Stopped" => ServiceRunState.Stopped,
        "Paused" => ServiceRunState.Paused,
        "Start Pending" or "Stop Pending" or "Continue Pending" or "Pause Pending" => ServiceRunState.Pending,
        _ => ServiceRunState.Unknown,
    };

    private static ServiceStartMode StartMode(string? mode) => mode switch
    {
        "Boot" => ServiceStartMode.Boot,
        "System" => ServiceStartMode.System,
        "Auto" => ServiceStartMode.Automatic,
        "Manual" => ServiceStartMode.Manual,
        "Disabled" => ServiceStartMode.Disabled,
        _ => ServiceStartMode.Unknown,
    };
}
