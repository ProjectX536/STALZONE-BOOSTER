using Booster.Core.Abstractions;
using Booster.Providers.Gpu.Amd;
using Booster.Providers.Gpu.Generic;
using Booster.Providers.Gpu.Intel;
using Booster.Providers.Gpu.Nvidia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Booster.Providers.Gpu;

public static class GpuProvidersDependencyInjection
{
    public static IServiceCollection AddBoosterGpuProviders(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IGpuProvider, GenericGpuProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IGpuProvider, AmdProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IGpuProvider, IntelGpuProvider>());
        services.TryAddSingleton<INvApiFactory, NvApiLibraryFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IGpuProvider, NvidiaProvider>());
        return services;
    }
}
