using System.Globalization;
using System.IO;
using Booster.Platform.Windows.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Core;

namespace Booster.App.Logging;

/// <summary>Журнал в файл: <c>%LOCALAPPDATA%\Booster\logs\booster-ГГГГММДД.log</c>, новый файл каждый день.</summary>
internal static class LoggingSetup
{
    public const string FileNamePattern = "booster-.log";

    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Логгер нужен раньше, чем DI-контейнер, а срок хранения журналов лежит в settings.json. Поэтому конфигурация
    /// читается здесь отдельно и без логирования; её предупреждения запишет экземпляр из контейнера.
    /// </summary>
    public static Logger CreateLogger(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var settings = new BoosterConfiguration(new ConfigFileSource(paths), NullLogger<BoosterConfiguration>.Instance)
            .Current.Settings.Logging;

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            // shared: окно и экспорт без окна могут работать одновременно и писать в один файл дня.
            .WriteTo.File(Path.Combine(paths.Logs, FileNamePattern), rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: settings.RetainedFileCount, shared: true, outputTemplate: OutputTemplate,
                formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();
    }
}
