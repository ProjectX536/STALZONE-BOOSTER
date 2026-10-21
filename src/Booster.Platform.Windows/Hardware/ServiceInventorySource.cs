using System.Security;
using Booster.Core.Abstractions;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Hardware;

/// <summary>
/// Службы через WMI, ServiceDll — из реестра, издатель — из ресурса версии файла. Только чтение.
/// Сбой запроса зависимостей не глушится: без него служба выглядела бы независимой и попала бы в кандидаты.
/// </summary>
public sealed class ServiceInventorySource : IServiceInventorySource
{
    private const string ServiceQuery =
        "SELECT Name, DisplayName, State, StartMode, DelayedAutoStart, PathName, ProcessId FROM Win32_Service";
    private const string DependencyQuery = "SELECT Antecedent, Dependent FROM Win32_DependentService";
    private const string ServiceDllValue = "ServiceDll";

    private readonly ICimClient _cim;
    private readonly IRegistryReader _registry;
    private readonly IFilePublisherSource _publishers;

    public ServiceInventorySource(ICimClient cim, IRegistryReader registry)
        : this(cim, registry, new FilePublisherSource())
    {
    }

    internal ServiceInventorySource(ICimClient cim, IRegistryReader registry, IFilePublisherSource publishers)
    {
        _cim = cim ?? throw new ArgumentNullException(nameof(cim));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _publishers = publishers ?? throw new ArgumentNullException(nameof(publishers));
    }

    public async Task<IReadOnlyList<ServiceInfo>> ReadAsync(CancellationToken ct)
    {
        var services = await _cim.QueryAsync(WmiNamespaces.CimV2, ServiceQuery, ct).ConfigureAwait(false);
        var dependencies = await _cim.QueryAsync(WmiNamespaces.CimV2, DependencyQuery, ct).ConfigureAwait(false);

        return ServiceInventoryMapper.Map(services, dependencies, ServiceDll, path =>
        {
            ct.ThrowIfCancellationRequested();
            return _publishers.GetPublisher(path);
        });
    }

    private string? ServiceDll(string name)
    {
        try
        {
            return _registry.GetValue(RegistryRoot.LocalMachine, $@"SYSTEM\CurrentControlSet\Services\{name}\Parameters", ServiceDllValue)
                as string;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            // Без ServiceDll издателем останется svchost (Microsoft) — служба не попадёт в кандидаты.
            return null;
        }
    }
}
