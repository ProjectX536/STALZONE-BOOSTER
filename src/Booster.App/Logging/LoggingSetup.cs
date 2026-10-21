using System.Globalization;
using System.IO;
using Booster.Platform.Windows.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Core;

namespace Booster.App.Logging;

internal static class LoggingSetup
{
    public const string FileNamePattern = "booster-.log";

    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    public static Logger CreateLogger(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var settings = new BoosterConfiguration(new ConfigFileSource(paths), NullLogger<BoosterConfiguration>.Instance)
            .Current.Settings.Logging;

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(Path.Combine(paths.Logs, FileNamePattern), rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: settings.RetainedFileCount, shared: true, outputTemplate: OutputTemplate,
                formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();
    }
}
