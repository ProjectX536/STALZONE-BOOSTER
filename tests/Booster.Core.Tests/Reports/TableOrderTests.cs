using System.Globalization;
using Booster.Core.Reports;

namespace Booster.Core.Tests.Reports;

public class TableOrderTests
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    [Fact]
    public void Sizes_are_compared_by_bytes_not_by_text()
    {
        Assert.Equal(["1023 Б", "1,5 КБ", "512 МБ", "1,2 ГБ", "2 ТБ"], Sorted(["2 ТБ", "1,2 ГБ", "512 МБ", "1,5 КБ", "1023 Б"]));
    }

    [Fact]
    public void Rates_are_compared_by_value_with_unit_prefix()
    {
        Assert.Equal(["900 кбит/с", "1,5 Мбит/с", "1 Гбит/с"], Sorted(["1 Гбит/с", "900 кбит/с", "1,5 Мбит/с"]));
        Assert.Equal(["800 КБ/с", "2 МБ/с"], Sorted(["2 МБ/с", "800 КБ/с"]));
    }

    [Fact]
    public void Numbers_are_compared_by_value()
    {
        // Разделитель разрядов ru-RU — неразрывный пробел.
        Assert.Equal(["-5 %", "3 %", "9,5 мс", "12,3 мс", "1 234 Гц"], Sorted(["12,3 мс", "1 234 Гц", "3 %", "9,5 мс", "-5 %"]));
    }

    [Fact]
    public void Numbers_inside_text_are_compared_naturally()
    {
        Assert.Equal(["Диск 2", "Диск 10", "диск 11"], Sorted(["Диск 10", "диск 11", "Диск 2"]));
    }

    [Fact]
    public void Text_is_compared_by_culture_ignoring_case()
    {
        Assert.Equal(["арбуз", "Ёж", "Яблоко"], Sorted(["Яблоко", "арбуз", "Ёж"]));
    }

    [Fact]
    public void Dotted_versions_are_compared_part_by_part()
    {
        Assert.Equal(["1.2.9", "1.2.10", "31.0.15.999", "31.0.15.3623"], Sorted(["31.0.15.3623", "1.2.10", "31.0.15.999", "1.2.9"]));
    }

    [Fact]
    public void Dates_are_compared_as_dates()
    {
        Assert.Equal(["15.03.2024", "02.11.2025", "21.10.2026 9:05:00"], Sorted(["02.11.2025", "21.10.2026 9:05:00", "15.03.2024"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unknown_values_go_last_in_both_directions(bool descending)
    {
        string[] values = ["Неизвестно", "5 ГБ", "—", "", "Нужны права администратора", "1 ГБ", "Не удалось получить", "Не поддерживается"];

        var sorted = values.Order(By<string>(descending)).ToList();

        Assert.Equal(descending ? ["5 ГБ", "1 ГБ"] : ["1 ГБ", "5 ГБ"], sorted.Take(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unavailable_cell_goes_last_whatever_its_text(bool descending)
    {
        var unavailable = new ReportCell("100 ГБ", null, CellTone.Unavailable);
        ReportCell[] cells = [unavailable, new("1 ГБ"), new("5 ГБ")];

        var sorted = cells.Order(By<ReportCell>(descending)).ToList();

        Assert.Same(unavailable, sorted[^1]);
    }

    [Fact]
    public void Comparable_values_of_one_type_are_compared_directly()
    {
        var early = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var late = early.AddDays(1);

        Assert.True(TableOrder.Compare(early, late, descending: false, Russian) < 0);
        Assert.True(TableOrder.Compare(early, late, descending: true, Russian) > 0);
        Assert.True(TableOrder.Compare(2, 10, descending: false, Russian) < 0);
        Assert.True(TableOrder.Compare(null, 1, descending: true, Russian) > 0);
    }

    [Fact]
    public void Sort_orders_rows_by_column_and_keeps_order_of_equal_rows()
    {
        var first = Row("a", "1 ГБ");
        var second = Row("b", "512 МБ");
        var third = Row("c", "1 ГБ");
        var shortRow = new ReportRow([new ReportCell("d")]);

        var sorted = TableOrder.Sort([first, second, shortRow, third], column: 1, descending: true, Russian);

        Assert.Equal([first, third, second, shortRow], sorted);
    }

    private static List<string> Sorted(string[] values) => [.. values.Order(By<string>(descending: false))];

    private static Comparer<T> By<T>(bool descending) =>
        Comparer<T>.Create((x, y) => TableOrder.Compare(x, y, descending, Russian));

    private static ReportRow Row(params string[] texts) => new([.. texts.Select(t => new ReportCell(t))]);
}
