using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;
using Booster.App.Tests.TestSupport;
using Booster.App.Views.Controls;
using Booster.Core.Reports;

namespace Booster.App.Tests.Views;

public class TableSortingTests
{
    public sealed record Item(string Name, string Size, Nested Inner);

    public sealed record Nested(int Order);

    [Fact]
    public void Click_sorts_ascending_then_descending_with_unknown_last()
    {
        var (ascending, descending, directions) = WpfHost.Invoke(() =>
        {
            var grid = Grid([Make("a", "1,2 ГБ"), Make("b", "Неизвестно"), Make("c", "512 МБ")]);
            var size = grid.Columns[1];

            TableSorting.SortBy(grid, size);
            var first = Names(grid);
            var firstDirection = size.SortDirection;
            TableSorting.SortBy(grid, size);
            return (first, Names(grid), (firstDirection, size.SortDirection));
        });

        Assert.Equal(["c", "a", "b"], ascending);
        Assert.Equal(["a", "c", "b"], descending);
        Assert.Equal((ListSortDirection.Ascending, ListSortDirection.Descending), directions);
    }

    [Fact]
    public void Sort_is_kept_when_rows_are_replaced()
    {
        var (names, direction) = WpfHost.Invoke(() =>
        {
            var grid = Grid([Make("a", "2 ГБ"), Make("b", "1 ГБ")]);
            TableSorting.SortBy(grid, grid.Columns[1]);

            grid.ItemsSource = Rows([Make("x", "5 МБ"), Make("y", "3 ГБ"), Make("z", "1 КБ")]);
            return (Names(grid), grid.Columns[1].SortDirection);
        });

        Assert.Equal(["z", "x", "y"], names);
        Assert.Equal(ListSortDirection.Ascending, direction);
    }

    [Fact]
    public void Sort_member_path_can_point_to_a_nested_value()
    {
        var names = WpfHost.Invoke(() =>
        {
            var grid = Grid([Make("a", "", order: 10), Make("b", "", order: 2)]);
            var column = new DataGridTextColumn { Binding = new Binding(nameof(Item.Name)), SortMemberPath = "Inner.Order" };
            grid.Columns.Add(column);

            TableSorting.SortBy(grid, column);
            return Names(grid);
        });

        Assert.Equal(["b", "a"], names);
    }

    [Fact]
    public void Report_columns_sort_by_their_cell()
    {
        var texts = WpfHost.Invoke(() =>
        {
            var grid = new DataGrid();
            TableSorting.SetIsEnabled(grid, true);
            ReportDataGrid.SetHeaders(grid, ["Имя", "Размер"]);
            grid.ItemsSource = new List<ReportRow>
            {
                new([new("a"), new("1 ГБ")]),
                new([new("b"), new("100 ГБ", Tone: CellTone.Unavailable)]),
                new([new("c"), new("2 МБ")]),
            };

            TableSorting.SortBy(grid, grid.Columns[1]);
            return grid.Items.Cast<ReportRow>().Select(r => r.Cells[0].Text).ToList();
        });

        Assert.Equal(["c", "a", "b"], texts);
    }

    private static DataGrid Grid(Item[] items)
    {
        var grid = new DataGrid();
        TableSorting.SetIsEnabled(grid, true);
        grid.Columns.Add(new DataGridTextColumn { Binding = new Binding(nameof(Item.Name)) });
        grid.Columns.Add(new DataGridTextColumn { Binding = new Binding(nameof(Item.Size)) });
        grid.ItemsSource = Rows(items);
        return grid;
    }

    // Как во вьюмоделях: список строк собирается выражением коллекции в IReadOnlyList.
    private static IReadOnlyList<Item> Rows(Item[] items) => [.. items.Select(i => i)];

    private static Item Make(string name, string size, int order = 0) => new(name, size, new Nested(order));

    private static List<string> Names(DataGrid grid) => [.. grid.Items.Cast<Item>().Select(i => i.Name)];
}
