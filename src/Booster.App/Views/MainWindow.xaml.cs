using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;
using Booster.App.Services;
using Booster.App.ViewModels;

namespace Booster.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel, LinkOpener links)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(links);
        InitializeComponent();
        DataContext = viewModel;

        AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler((_, e) =>
        {
            links.TryOpen(e.Uri);
            e.Handled = true;
        }));
        Loaded += (_, _) => viewModel.Start();
    }
}
