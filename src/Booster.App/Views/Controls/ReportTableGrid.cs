using System.Windows;
using System.Windows.Controls;
using Booster.Core.Reports;

namespace Booster.App.Views.Controls;

/// <summary>
/// Небольшая таблица отчёта статичной сеткой — для таблиц внутри прокручиваемой страницы, где у DataGrid была бы своя
/// прокрутка. Ячейки показывает шаблон <see cref="ReportCell"/> из ресурсов; строка с замечанием подсвечена.
/// Длинные списки (драйверы) показывает DataGrid с <see cref="ReportDataGrid"/>.
/// </summary>
public sealed class ReportTableGrid : ContentControl
{
    public static readonly DependencyProperty TableProperty = DependencyProperty.Register(
        nameof(Table), typeof(ReportTable), typeof(ReportTableGrid), new PropertyMetadata(null, (d, _) => ((ReportTableGrid)d).Build()));

    public ReportTableGrid()
    {
        Focusable = false;
        IsTabStop = false;
    }

    public ReportTable? Table
    {
        get => (ReportTable?)GetValue(TableProperty);
        set => SetValue(TableProperty, value);
    }

    private void Build()
    {
        if (Table is not { } table)
        {
            Content = null;
            return;
        }

        var grid = new Grid();
        foreach (var _ in table.Headers)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var column = 0; column < table.Headers.Count; column++)
        {
            var header = new TextBlock { Text = table.Headers[column], FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            Place(grid, Cell("ReportHeaderCell", header), 0, column);
        }

        for (var row = 0; row < table.Rows.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var cells = table.Rows[row];
            var style = cells.Flagged ? "ReportFlaggedCell" : "ReportDataCell";
            for (var column = 0; column < Math.Min(cells.Cells.Count, table.Headers.Count); column++)
                Place(grid, Cell(style, new ContentPresenter { Content = cells.Cells[column] }), row + 1, column);
        }

        // Ячейки рисуют правую и нижнюю границы, рамка — левую и верхнюю.
        var frame = new Border { Child = grid };
        frame.SetResourceReference(StyleProperty, "ReportTableFrame");
        Content = frame;
    }

    private static Border Cell(string styleKey, UIElement content)
    {
        var cell = new Border { Child = content };
        cell.SetResourceReference(StyleProperty, styleKey);
        return cell;
    }

    private static void Place(Grid grid, UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }
}
