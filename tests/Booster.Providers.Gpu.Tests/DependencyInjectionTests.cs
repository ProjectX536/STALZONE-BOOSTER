using Booster.Core;
using Booster.Core.Abstractions;
using Booster.Core.Sensors;
using Booster.Platform.Windows;
using Booster.Platform.Windows.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booster.Providers.Gpu.Tests;

public sealed class DependencyInjectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Booster.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Gpu_providers_are_registered()
    {
        // Чтобы тест не создавал каталоги в настоящем %LOCALAPPDATA%.
        var services = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IAppPaths>(new AppPaths(Path.Combine(_root, "data"), AppContext.BaseDirectory))
            .AddBoosterCore()
            .AddBoosterPlatformWindows()
            .AddBoosterGpuProviders()
            .AddBoosterGpuProviders();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.Equal(["providers.gpu.amd", "providers.gpu.generic", "providers.gpu.intel", "providers.gpu.nvidia"],
            provider.GetServices<IGpuProvider>().Select(p => p.ProviderId).Order(StringComparer.Ordinal));
        Assert.IsType<SensorProvider>(provider.GetRequiredService<ISensorProvider>());
    }
}
