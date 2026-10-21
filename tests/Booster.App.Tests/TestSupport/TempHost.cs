using Booster.App.Composition;
using Booster.Platform.Windows.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Booster.App.Tests.TestSupport;

/// <summary>
/// Корень композиции Booster с данными во временном каталоге. Хост не запускается: восстановление, координатор и замеры
/// не стартуют, система не меняется.
/// </summary>
internal sealed class TempHost : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "Booster.App.Tests", Guid.NewGuid().ToString("N"));
    private readonly IHost _host;

    public TempHost()
    {
        _host = AppHost.Build(new AppPaths(_dataRoot, AppContext.BaseDirectory), new LoggerConfiguration().CreateLogger());
    }

    public T Resolve<T>()
        where T : notnull => (T)Resolve(typeof(T));

    /// <summary>
    /// Окно создаёт экраны в UI-потоке, и часть из них запоминает его контекст синхронизации. Здесь UI-поток заменяет
    /// простой контекст, который выполняет отправленное в пуле потоков.
    /// </summary>
    public object Resolve(Type type)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
        try
        {
            return _host.Services.GetRequiredService(type);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    public void Dispose()
    {
        _host.Dispose();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }
}
