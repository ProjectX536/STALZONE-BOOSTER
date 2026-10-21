using System.Globalization;
using Booster.Platform.Windows.Common;
using static Booster.Platform.Windows.Tests.TestSupport.WmiFixture;

namespace Booster.Platform.Windows.Tests.Common;

public class CimRowTests
{
    public static TheoryData<object> SixInAnyIntegerForm => [6L, 6u, 6, (ushort)6, (byte)6, 6UL, "6", " 6 "];

    [Theory]
    [MemberData(nameof(SixInAnyIntegerForm))]
    public void Integer_is_read_from_any_integer_type_and_numeric_string(object value)
    {
        Assert.Equal(6, Row(("NumberOfCores", value)).GetInt32("NumberOfCores"));
        Assert.Equal(6L, Row(("NumberOfCores", value)).GetInt64("NumberOfCores"));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("6.0")]
    [InlineData(true)]
    public void Non_integer_value_gives_null(object value)
    {
        Assert.Null(Row(("X", value)).GetInt32("X"));
    }

    [Fact]
    public void Int32_out_of_range_gives_null_instead_of_overflow()
    {
        var row = Row(("Size", 512110190592L));

        Assert.Null(row.GetInt32("Size"));
        Assert.Equal(512110190592L, row.GetInt64("Size"));
    }

    [Fact]
    public void Ulong_above_int64_range_gives_null()
    {
        Assert.Null(Row(("X", ulong.MaxValue)).GetInt64("X"));
    }

    [Fact]
    public void Decimal_string_is_not_read_as_integer_under_ru_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

            Assert.Null(Row(("X", "1.5")).GetInt64("X"));
            Assert.Null(Row(("X", "1,5")).GetInt64("X"));
            Assert.Null(Row(("X", "1 500")).GetInt64("X"));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void String_is_trimmed()
    {
        Assert.Equal("Intel(R) Core(TM)", Row(("Name", "  Intel(R) Core(TM)  ")).GetString("Name"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\0")]
    public void Empty_string_gives_null(string value)
    {
        Assert.Null(Row(("Name", value)).GetString("Name"));
    }

    [Fact]
    public void Char_is_read_as_string_and_zero_char_as_null()
    {
        Assert.Equal("C", Row(("DriveLetter", 'C')).GetString("DriveLetter"));
        Assert.Null(Row(("DriveLetter", '\0')).GetString("DriveLetter"));
    }

    [Fact]
    public void Missing_property_gives_null_and_empty_arrays()
    {
        var row = Row(("Name", "x"));

        Assert.Null(row.GetString("Other"));
        Assert.Null(row.GetInt32("Other"));
        Assert.Null(row.GetBoolean("Other"));
        Assert.Empty(row.GetStringArray("CompatibleID"));
        Assert.Empty(row.GetInt32Array("ChassisTypes"));
    }

    [Fact]
    public void Property_names_are_case_insensitive()
    {
        var row = Row(("NumberOfCores", 10L));

        Assert.Equal(10, row.GetInt32("numberofcores"));
        Assert.Equal(10, row.GetInt32("NUMBEROFCORES"));
    }

    [Fact]
    public void String_array_skips_empty_items_and_trims()
    {
        var row = Row(("HardwareID", new[] { "PCI\\VEN_10DE&DEV_2489", "  ", " PCI\\VEN_10DE " }));

        Assert.Equal(["PCI\\VEN_10DE&DEV_2489", "PCI\\VEN_10DE"], row.GetStringArray("HardwareID"));
    }

    [Fact]
    public void String_array_accepts_object_array_and_null_property()
    {
        Assert.Equal(["a", "b"], Row(("X", new object?[] { "a", null, "b" })).GetStringArray("X"));
        Assert.Empty(Row(("X", null)).GetStringArray("X"));
    }

    [Fact]
    public void Int32_array_accepts_ushort_array_and_fixture_long_array()
    {
        Assert.Equal([3], Row(("ChassisTypes", new ushort[] { 3 })).GetInt32Array("ChassisTypes"));
        Assert.Equal([10, 9], Row(("ChassisTypes", new object?[] { 10L, 9L })).GetInt32Array("ChassisTypes"));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    public void Boolean_is_read_from_bool_and_string(object value, bool expected)
    {
        Assert.Equal(expected, Row(("IsSigned", value)).GetBoolean("IsSigned"));
    }

    [Fact]
    public void Boolean_from_garbage_is_null()
    {
        Assert.Null(Row(("IsSigned", "maybe")).GetBoolean("IsSigned"));
    }

    [Fact]
    public void Fixture_row_reads_real_values()
    {
        var cpu = LoadSingle("desktop-nvidia/Win32_Processor.json");

        Assert.Equal("Intel(R) Core(TM) i5-14400F", cpu.GetString("Name"));
        Assert.Equal(10, cpu.GetInt32("NumberOfCores"));
        Assert.Equal(16, cpu.GetInt32("NumberOfLogicalProcessors"));
    }
}
