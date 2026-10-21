using System.Windows;
using System.Windows.Threading;

namespace Booster.App.Tests.TestSupport;

internal static class WpfHost
{
    private static readonly Lazy<Dispatcher> UiDispatcher = new(Start);

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

    public static T Invoke<T>(Func<T> action) => UiDispatcher.Value.Invoke(action);

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
