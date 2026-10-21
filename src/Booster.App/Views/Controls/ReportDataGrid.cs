using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Booster.Core.Reports;

namespace Booster.App.Views.Controls;

public static class ReportDataGrid
{
    public static readonly DependencyProperty HeadersProperty = DependencyProperty.RegisterAttached(
        "Headers", typeof(IReadOnlyList<string>), typeof(ReportDataGrid), new PropertyMetadata(null, OnHeadersChanged));

    private static readonly Thickness CellMargin = new(6, 3, 6, 3);

    public static IReadOnlyList<string>? GetHeaders(DataGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        return (IReadOnlyList<string>?)grid.GetValue(HeadersProperty);
    }

    public static void SetHeaders(DataGrid grid, IReadOnlyList<string>? value)
    {
        ArgumentNullException.ThrowIfNull(grid);
        grid.SetValue(HeadersProperty, value);
    }

    private static void OnHeadersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid)
            return;

        grid.Columns.Clear();
        if (e.NewValue is not IReadOnlyList<string> headers)
            return;

        for (var i = 0; i < headers.Count; i++)
        {
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetBinding(ContentPresenter.ContentProperty, new Binding($"{nameof(ReportRow.Cells)}[{i}]"));
            presenter.SetValue(FrameworkElement.MarginProperty, CellMargin);
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = headers[i],
                CellTemplate = new DataTemplate { VisualTree = presenter },
                ClipboardContentBinding = new Binding($"{nameof(ReportRow.Cells)}[{i}].{nameof(ReportCell.Text)}"),
                SortMemberPath = $"{nameof(ReportRow.Cells)}[{i}]",
            });
        }
    }
}
