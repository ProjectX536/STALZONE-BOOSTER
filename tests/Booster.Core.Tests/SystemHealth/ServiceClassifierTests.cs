using Booster.Core.Abstractions;
using Booster.Core.Reports;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.SystemHealth;

public class ServiceClassifierTests
{
    private const string Source = "test";
    private static readonly Booster.Core.Configuration.ServiceCatalog Catalog = FakeConfiguration.DefaultCatalog.Services;

    private static ServiceInfo Service(string name, string? displayName = null, string? publisher = "Contoso Ltd.",
        ServiceStartMode startMode = ServiceStartMode.Automatic, ServiceRunState state = ServiceRunState.Running,
        params string[] dependents) =>
        new(name, displayName ?? name, state, startMode, false, $@"C:\Program Files\{name}\{name}.exe", 1234,
            publisher is null ? Reading<string>.Unknown(Source) : Reading<string>.Known(publisher, Source), dependents);

    private static ServiceCategory Classify(ServiceInfo service) => ServiceClassifier.Classify(service, Catalog);

    [Fact]
    public void Catalog_names_win_over_publisher()
    {
        Assert.Equal(ServiceCategory.SystemCritical, Classify(Service("RpcSs", publisher: "Microsoft Corporation")));
        Assert.Equal(ServiceCategory.SystemCritical, Classify(Service("rpcss", publisher: "Contoso Ltd.")));
        Assert.Equal(ServiceCategory.Security, Classify(Service("WinDefend", publisher: "Microsoft Corporation")));
    }

    [Fact]
    public void Security_keyword_in_display_name()
    {
        Assert.Equal(ServiceCategory.Security, Classify(Service("AVP21.3", "Kaspersky Security Service", "AO Kaspersky Lab")));
        Assert.Equal(ServiceCategory.Security, Classify(Service("ekrn", "ESET Service", publisher: null)));
    }

    [Fact]
    public void Anti_cheat_by_name_and_keyword()
    {
        Assert.Equal(ServiceCategory.AntiCheat, Classify(Service("EasyAntiCheat_EOS", publisher: "Epic Games, Inc.")));
        Assert.Equal(ServiceCategory.AntiCheat, Classify(Service("XyzGuard", "BattlEye Service", "BattlEye Innovations")));
    }

    [Fact]
    public void Microsoft_publisher()
    {
        Assert.Equal(ServiceCategory.Microsoft, Classify(Service("WSearch", "Windows Search", "Microsoft Corporation")));
    }

    [Fact]
    public void Device_keyword_for_third_party()
    {
        Assert.Equal(ServiceCategory.DeviceOrNetwork,
            Classify(Service("NVDisplay.ContainerLocalSystem", "NVIDIA Display Container LS", "NVIDIA Corporation")));
        Assert.Equal(ServiceCategory.DeviceOrNetwork, Classify(Service("RtkAudioUniversalService", "Realtek Audio Universal Service",
            publisher: null)));
    }

    [Fact]
    public void Third_party_by_publisher()
    {
        Assert.Equal(ServiceCategory.ThirdParty, Classify(Service("ContosoUpdater", "Contoso Updater", "Contoso Ltd.")));
    }

    [Fact]
    public void Service_with_unreadable_image_is_unknown()
    {
        var service = Service("ContosoUpdater", "Contoso Updater") with
        {
            Publisher = Reading<string>.Failure(Source, "UnauthorizedAccessException: denied"),
        };

        var assessment = Assert.Single(ServiceClassifier.Assess([service], Catalog));

        Assert.Equal(ServiceCategory.Unknown, assessment.Category);
        Assert.False(assessment.ReviewCandidate);
    }

    [Fact]
    public void Review_candidate_requires_auto_running_and_no_dependents()
    {
        var assessed = ServiceClassifier.Assess(
        [
            Service("Candidate"),
            Service("Manual", startMode: ServiceStartMode.Manual),
            Service("Stopped", state: ServiceRunState.Stopped),
            Service("WithDependents", dependents: "ContosoHelper"),
            Service("MsService", publisher: "Microsoft Corporation"),
        ], Catalog);

        Assert.Equal(["Candidate"], assessed.Where(a => a.ReviewCandidate).Select(a => a.Service.Name));
        Assert.All(assessed, a => Assert.Equal(a.Service.Name == "MsService" ? ServiceCategory.Microsoft : ServiceCategory.ThirdParty,
            a.Category));
    }

    [Fact]
    public void Advice_counts_candidates()
    {
        var assessed = ServiceClassifier.Assess([Service("A"), Service("B"), Service("RpcSs")], Catalog);

        var advice = Assert.Single(ServiceClassifier.Advise(assessed));

        Assert.Equal(AdviceId.ThirdPartyServicesAutostart, advice.Id);
        Assert.Equal(AdviceModule.Services, advice.Module);
        Assert.Equal(FindingSeverity.Info, advice.Severity);
        Assert.Equal(SafetyLevel.L3ManualOnly, advice.Level);
        Assert.Equal(["2"], advice.Arguments);
    }

    [Fact]
    public void No_candidates_no_advice()
    {
        var assessed = ServiceClassifier.Assess([Service("RpcSs"), Service("A", startMode: ServiceStartMode.Manual)], Catalog);

        Assert.Empty(ServiceClassifier.Advise(assessed));
        Assert.Empty(ServiceClassifier.Advise([]));
    }
}
