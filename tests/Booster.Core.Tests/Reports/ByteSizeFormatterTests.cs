using System.Globalization;
using Booster.Core.Reports;

namespace Booster.Core.Tests.Reports;

public class ByteSizeFormatterTests
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    [Theory]
    [InlineData(8_589_934_592L, "8 ГБ")]
    [InlineData(17_013_911_552L, "15,8 ГБ")]
    [InlineData(1_572_864L, "1,5 МБ")]
    [InlineData(0L, "0 Б")]
    [InlineData(1023L, "1023 Б")]
    [InlineData(1536L, "1,5 КБ")]
    [InlineData(2_199_023_255_552L, "2 ТБ")]
    public void Formats_with_base_1024_and_one_decimal(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSizeFormatter.Format(bytes, Russian));
    }

    [Fact]
    public void Value_that_rounds_up_to_1024_moves_to_the_next_unit()
    {
        // 1 048 575 Б = 1023,999 КБ: округление дало бы «1024 КБ».
        Assert.Equal("1 МБ", ByteSizeFormatter.Format(1_048_575L, Russian));
    }

    [Fact]
    public void Decimal_separator_follows_culture()
    {
        Assert.Equal("1.5 МБ", ByteSizeFormatter.Format(1_572_864L, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Negative_size_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ByteSizeFormatter.Format(-1, Russian));
    }
}
