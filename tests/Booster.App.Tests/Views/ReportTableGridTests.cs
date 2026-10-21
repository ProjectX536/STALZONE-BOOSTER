using Booster.App.Tests.TestSupport;
using Booster.App.Views.Controls;
using Booster.Core.Reports;

namespace Booster.App.Tests.Views;

public class ReportTableGridTests
{
    [Fact]
    public void Header_click_sorts_ascending_then_descending_with_unknown_last()
    {
        var (ascending, descending) = WpfHost.Invoke(() =>
        {
            var grid = new ReportTableGrid { Table = Table(("a", "1,2 ГБ"), ("b", "Неизвестно"), ("c", "512 МБ")) };

            grid.SortBy(1);
            var first = Names(grid);
            grid.SortBy(1);
            return (first, Names(grid));
        });

        Assert.Equal(["c", "a", "b"], ascending);
        Assert.Equal(["a", "c", "b"], descending);
    }

    [Fact]
    public void Other_column_starts_ascending()
    {
        var names = WpfHost.Invoke(() =>
        {
            var grid = new ReportTableGrid { Table = Table(("b", "1 ГБ"), ("a", "2 ГБ")) };

            grid.SortBy(1);
            grid.SortBy(1);
            grid.SortBy(0);
            return Names(grid);
        });

        Assert.Equal(["a", "b"], names);
    }

    [Fact]
    public void Sort_is_kept_when_table_is_replaced()
    {
        var names = WpfHost.Invoke(() =>
        {
            var grid = new ReportTableGrid { Table = Table(("a", "2 ГБ"), ("b", "1 ГБ")) };
            grid.SortBy(1);

            grid.Table = Table(("x", "5 МБ"), ("y", "3 ГБ"), ("z", "1 КБ"));
            return Names(grid);
        });

        Assert.Equal(["z", "x", "y"], names);
    }

    [Fact]
    public void Unsorted_table_keeps_report_order()
    {
        var names = WpfHost.Invoke(() => Names(new ReportTableGrid { Table = Table(("b", "1"), ("a", "2")) }));

        Assert.Equal(["b", "a"], names);
    }

    private static ReportTable Table(params (string Name, string Size)[] rows) =>
        new(["Имя", "Размер"], [.. rows.Select(r => new ReportRow([new(r.Name), new(r.Size)]))]);

    private static List<string> Names(ReportTableGrid grid) => [.. grid.DisplayedRows.Select(r => r.Cells[0].Text)];
}
