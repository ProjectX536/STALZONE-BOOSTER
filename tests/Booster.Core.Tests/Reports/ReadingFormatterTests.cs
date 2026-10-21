using Booster.Core.Abstractions;
using Booster.Core.Reports;

namespace Booster.Core.Tests.Reports;

public class ReadingFormatterTests
{
    [Fact]
    public void Available_value_is_formatted_by_the_caller()
    {
        Assert.Equal("16 потоков", ReadingFormatter.Format(Reading<int>.Known(16, "test"), v => $"{v} потоков"));
    }

    [Theory]
    [InlineData(DataStatus.Unknown, "Неизвестно")]
    [InlineData(DataStatus.RequiresAdmin, "Нужны права администратора")]
    [InlineData(DataStatus.NotSupported, "Не поддерживается")]
    [InlineData(DataStatus.Failed, "Не удалось получить")]
    public void Unavailable_value_shows_status_text(DataStatus status, string expected)
    {
        var reading = new Reading<int>(42, status, "test");

        Assert.Equal(expected, ReadingFormatter.Format(reading, _ => throw new InvalidOperationException("must not be called")));
    }

    [Fact]
    public void Every_status_has_its_own_text()
    {
        var texts = Enum.GetValues<DataStatus>().Select(ReadingFormatter.StatusText).ToList();

        Assert.Equal(texts.Count, texts.Distinct().Count());
    }

    [Fact]
    public void Available_reading_without_value_is_unknown()
    {
        Assert.Equal("Неизвестно", ReadingFormatter.Format(new Reading<string>(null, DataStatus.Available, "test"), v => v));
    }
}
