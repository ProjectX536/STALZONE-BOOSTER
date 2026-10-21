using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Reports;

namespace Booster.Core.SystemHealth;

/// <summary>Назначение службы. Всё, кроме ThirdParty, Booster к проверке не предлагает.</summary>
public enum ServiceCategory
{
    SystemCritical,
    Security,
    AntiCheat,
    DeviceOrNetwork,
    Microsoft,
    ThirdParty,
    Unknown,
}

/// <param name="ReviewCandidate">Сторонняя служба, которую пользователь может проверить сам. Booster её не трогает.</param>
public sealed record ServiceAssessment(ServiceInfo Service, ServiceCategory Category, bool ReviewCandidate);

/// <summary>
/// Классификация служб по справочнику. Первое совпадение сверху вниз: справочник важнее издателя, а служба,
/// чей издатель не прочитан, остаётся Unknown — неизвестное не предлагается к проверке.
/// </summary>
public static class ServiceClassifier
{
    private const string MicrosoftPublisher = "Microsoft";

    public static ServiceCategory Classify(ServiceInfo service, ServiceCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(catalog);

        if (HasName(catalog.SystemCritical, service))
            return ServiceCategory.SystemCritical;
        if (HasName(catalog.Security, service) || HasKeyword(catalog.SecurityKeywords, service))
            return ServiceCategory.Security;
        if (HasName(catalog.AntiCheat, service) || HasKeyword(catalog.AntiCheatKeywords, service))
            return ServiceCategory.AntiCheat;

        var publisher = service.Publisher.IsAvailable ? service.Publisher.Value : null;
        if (publisher is not null && publisher.Contains(MicrosoftPublisher, StringComparison.OrdinalIgnoreCase))
            return ServiceCategory.Microsoft;
        if (HasKeyword(catalog.DeviceKeywords, service))
            return ServiceCategory.DeviceOrNetwork;
        return publisher is not null ? ServiceCategory.ThirdParty : ServiceCategory.Unknown;
    }

    public static IReadOnlyList<ServiceAssessment> Assess(IReadOnlyList<ServiceInfo> services, ServiceCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(catalog);

        return services.Select(service =>
        {
            var category = Classify(service, catalog);
            var candidate = category == ServiceCategory.ThirdParty
                && service.StartMode == ServiceStartMode.Automatic
                && service.State == ServiceRunState.Running
                && service.DependentServices.Count == 0;
            return new ServiceAssessment(service, category, candidate);
        }).ToArray();
    }

    public static IReadOnlyList<SystemAdvice> Advise(IReadOnlyList<ServiceAssessment> assessed)
    {
        ArgumentNullException.ThrowIfNull(assessed);

        var candidates = assessed.Count(a => a.ReviewCandidate);
        return candidates == 0
            ? []
            : [new SystemAdvice(AdviceId.ThirdPartyServicesAutostart, AdviceModule.Services, FindingSeverity.Info, SafetyLevel.L3ManualOnly,
                [candidates.ToString(CultureInfo.InvariantCulture)])];
    }

    private static bool HasName(IReadOnlyList<string> names, ServiceInfo service) =>
        names.Contains(service.Name, StringComparer.OrdinalIgnoreCase);

    private static bool HasKeyword(IReadOnlyList<string> keywords, ServiceInfo service)
    {
        var publisher = service.Publisher.IsAvailable ? service.Publisher.Value : null;
        return keywords.Any(keyword =>
            service.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || service.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || (publisher?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false));
    }
}
