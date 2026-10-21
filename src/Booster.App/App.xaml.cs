using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using Booster.App.Composition;
using Booster.App.Headless;
using Booster.App.Logging;
using Booster.App.Resources;
using Booster.App.Views;
using Booster.Platform.Windows.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Booster.App;

public partial class App : Application
{
    private const int FailureExitCode = 1;
    private static readonly TimeSpan HostStopTimeout = TimeSpan.FromSeconds(5);

    private IHost? _host;
    private SingleInstanceLock? _instance;
    private ILogger<App>? _logger;
    private string? _logsDirectory;
    private bool _headless;
    private int _showingError;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _headless = ExportCommand.TryParse(e.Args, out var exportDirectory);

        // Экспорт только читает; второе окно восстановило бы при старте сессию, которую ведёт первое.
        if (!_headless && (_instance = SingleInstanceLock.TryAcquire(SingleInstanceLock.DefaultName)) is null)
        {
            MessageBox.Show(Strings.AlreadyRunning, Strings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var paths = new AppPaths();
        _logsDirectory = paths.Logs;
        _host = AppHost.Build(paths, LoggingSetup.CreateLogger(paths));
        _logger = _host.Services.GetRequiredService<ILogger<App>>();
        _host.Start();
        LogStarted(_logger, _headless ? "export" : "window");

        if (_headless)
        {
            RunExport(exportDirectory);
            return;
        }

        // Иначе WPF форматирует привязки по en-US, а не по региональным настройкам пользователя.
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_logger is not null)
            LogExiting(_logger, e.ApplicationExitCode);

        if (_host is not null)
        {
            try
            {
                // Остановка вне UI-потока: продолжения внутри хоста не должны ждать занятый диспетчер.
                Task.Run(() => _host.StopAsync(HostStopTimeout)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // Сессия не успела восстановиться за срок остановки: освобождение координатора дождётся восстановления.
                if (_logger is not null)
                    LogStopFailed(_logger, ex);
            }
            finally
            {
                Task.Run(_host.Dispose).GetAwaiter().GetResult();
            }
        }

        _instance?.Dispose();
        base.OnExit(e);
    }

    // async void: исключение, если оно всё же вылетит, попадёт в DispatcherUnhandledException и завершит экспорт с кодом 1.
    private async void RunExport(string? directory)
    {
        var code = await _host!.Services.GetRequiredService<ExportCommand>().RunAsync(directory, CancellationToken.None);
        Shutdown(code);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        LogUnexpected(e.Exception, "dispatcher");
        ShowErrorMessage();

        // Без окна приложению больше нечего делать: экспорт или запуск не завершились.
        if (_headless || MainWindow is not { IsLoaded: true })
            Shutdown(FailureExitCode);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // Процесс завершается: сообщение показывается сразу в потоке ошибки, журнал сбрасывается на диск.
        LogUnexpected(e.ExceptionObject as Exception, "appdomain");
        if (e.IsTerminating)
            _host?.Dispose();
        ShowErrorMessage();
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        LogUnexpected(e.Exception, "task");

        // Событие приходит в потоке финализатора; окно сообщения открывается в UI-потоке.
        Dispatcher.BeginInvoke(ShowErrorMessage);
    }

    private void LogUnexpected(Exception? exception, string source)
    {
        if (_logger is not null)
            LogUnhandled(_logger, exception, source);
    }

    private void ShowErrorMessage()
    {
        if (_headless || Interlocked.Exchange(ref _showingError, 1) == 1)
            return;

        try
        {
            MessageBox.Show(string.Format(CultureInfo.CurrentCulture, Strings.UnexpectedError, _logsDirectory),
                Strings.ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Interlocked.Exchange(ref _showingError, 0);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Booster started, mode {Mode}")]
    private static partial void LogStarted(ILogger logger, string mode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Booster exiting with code {ExitCode}")]
    private static partial void LogExiting(ILogger logger, int exitCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Host did not stop cleanly; disposing it anyway")]
    private static partial void LogStopFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception ({Source})")]
    private static partial void LogUnhandled(ILogger logger, Exception? exception, string source);
}
