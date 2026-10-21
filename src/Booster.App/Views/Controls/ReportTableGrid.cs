using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Booster.Core.Reports;

namespace Booster.App.Views.Controls;

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

    private const string AscendingMark = " ▲";
    private const string DescendingMark = " ▼";

    private int _sortColumn = -1;
    private bool _descending;

    internal IReadOnlyList<ReportRow> DisplayedRows { get; private set; } = [];

    internal void SortBy(int column)
    {
        _descending = column == _sortColumn && !_descending;
        _sortColumn = column;
        Build();
    }

    private void Build()
    {
        if (Table is not { } table)
        {
            DisplayedRows = [];
            Content = null;
            return;
        }

        var sorted = _sortColumn >= 0 && _sortColumn < table.Headers.Count;
        DisplayedRows = sorted ? TableOrder.Sort(table.Rows, _sortColumn, _descending, CultureInfo.CurrentCulture) : table.Rows;

        var grid = new Grid();
        foreach (var _ in table.Headers)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var column = 0; column < table.Headers.Count; column++)
        {
            var mark = sorted && column == _sortColumn ? _descending ? DescendingMark : AscendingMark : string.Empty;
            var header = new TextBlock { Text = table.Headers[column] + mark, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            var cell = Cell("ReportHeaderCell", header);
            cell.Cursor = Cursors.Hand;
            var index = column;
            cell.MouseLeftButtonUp += (_, _) => SortBy(index);
            Place(grid, cell, 0, column);
        }

        for (var row = 0; row < DisplayedRows.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var cells = DisplayedRows[row];
            var style = cells.Flagged ? "ReportFlaggedCell" : "ReportDataCell";
            for (var column = 0; column < Math.Min(cells.Cells.Count, table.Headers.Count); column++)
                Place(grid, Cell(style, new ContentPresenter { Content = cells.Cells[column] }), row + 1, column);
        }

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
