using Booster.App.Services;
using Booster.App.Tests.TestSupport;
using Booster.App.ViewModels;
using Booster.Core.SystemHealth;

namespace Booster.App.Tests.Composition;

public sealed class AppHostTests
{
    [Fact]
    public void Host_resolves_every_page_view_model()
    {
        using var host = new TempHost();

        var main = host.Resolve<MainViewModel>();

        Assert.Equal(Enum.GetValues<AppPage>(), main.Pages.Select(p => p.Page));
        Assert.All(main.Pages, page => Assert.Same(host.Resolve(page.Content.GetType()), page.Content));
        Assert.All(main.Pages.Where(p => p.Page is AppPage.Performance or AppPage.Profiles or AppPage.Journal or AppPage.Settings),
            page => Assert.IsAssignableFrom<IPage>(page.Content));
    }

    [Fact]
    public void Host_resolves_system_diagnostics()
    {
        using var host = new TempHost();

        Assert.NotNull(host.Resolve<DiagnosticsSession>());
        Assert.NotNull(host.Resolve<SystemDiagnosticsCollector>());
        Assert.NotNull(host.Resolve<NetworkTester>());
    }
}
