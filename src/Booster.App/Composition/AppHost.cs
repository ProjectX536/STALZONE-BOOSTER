using Booster.App.Headless;
using Booster.App.Services;
using Booster.App.ViewModels;
using Booster.App.Views;
using Booster.Core;
using Booster.Platform.Windows;
using Booster.Platform.Windows.Common;
using Booster.Providers.Gpu;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Booster.App.Composition;

internal static class AppHost
{
    public static IHost Build(IAppPaths paths, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "Booster",
            ContentRootPath = paths.BaseDirectory,
        });
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));
        builder.Services.Configure<ConsoleLifetimeOptions>(o => o.SuppressStatusMessages = true);
        builder.Services.AddSerilog(logger, dispose: true);

        builder.Services.AddSingleton(paths);
        builder.Services.AddBoosterCore().AddBoosterPlatformWindows().AddBoosterGpuProviders();

        builder.Services.AddSingleton<DiagnosticsSession>();
        builder.Services.AddSingleton<ReportExportService>();
        builder.Services.AddSingleton<ExportCommand>();
        builder.Services.AddSingleton<LinkOpener>();
        builder.Services.AddSingleton<ISettingsOpener>(sp => sp.GetRequiredService<LinkOpener>());
        builder.Services.AddSingleton<FilePicker>();
        builder.Services.AddSingleton<IGameExecutablePicker>(sp => sp.GetRequiredService<FilePicker>());
        builder.Services.AddSingleton<IBenchmarkCuePlayer, BenchmarkCuePlayer>();
        builder.Services.AddSingleton<SessionReport>();
        builder.Services.AddSingleton<GamingLifecycle>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<GamingLifecycle>());
        builder.Services.AddSingleton<StatusBarViewModel>();
        builder.Services.AddSingleton<OverviewViewModel>();
        builder.Services.AddSingleton<HardwareViewModel>();
        builder.Services.AddSingleton<DriversViewModel>();
        builder.Services.AddSingleton<LatencyViewModel>();
        builder.Services.AddSingleton<HagsViewModel>();
        builder.Services.AddSingleton<SystemViewModel>();
        builder.Services.AddSingleton<PerformanceViewModel>();
        builder.Services.AddSingleton<SessionViewModel>();
        builder.Services.AddSingleton<ProfilesViewModel>();
        builder.Services.AddSingleton<JournalViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddTransient<MainWindow>();
        return builder.Build();
    }
}
