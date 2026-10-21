using System.Windows;
using System.Windows.Threading;

namespace Booster.App.Tests.TestSupport;

/// <summary>
/// Один STA-поток с <see cref="Application"/> и ресурсами приложения на весь тестовый процесс: WPF допускает только один
/// Application на процесс, а классы тестов идут параллельно. Экран создаётся и раскладывается на этом потоке.
/// </summary>
internal static class WpfHost
{
    private static readonly Lazy<Dispatcher> UiDispatcher = new(Start);

    /// <summary>Создаёт элемент, раскладывает его и возвращает исключение загрузки XAML или привязок, если оно было.</summary>
    public static Exception? TryRender(Func<FrameworkElement> create)
    {
        try
        {
            UiDispatcher.Value.Invoke(() =>
            {
                var view = create();
                view.Measure(new Size(1200, 900));
                view.Arrange(new Rect(0, 0, 1200, 900));
                view.UpdateLayout();
            });
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static Dispatcher Start()
    {
        using var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Booster.App;component/Views/ReportTemplates.xaml"),
                });
                dispatcher = Dispatcher.CurrentDispatcher;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                ready.Set();
            }
            if (failure is null)
                Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WpfHost",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher ?? throw new InvalidOperationException("WPF host thread failed to start", failure);
    }
}
