using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Tests.Common;

public class OemPlaceholdersTests
{
    [Theory]
    [InlineData("To Be Filled By O.E.M.")]
    [InlineData("to be filled by o.e.m.")]
    [InlineData("Default string")]
    [InlineData("DEFAULT STRING")]
    [InlineData("System Product Name")]
    [InlineData("System manufacturer")]
    [InlineData("System Version")]
    [InlineData("Not Applicable")]
    [InlineData("O.E.M.")]
    [InlineData("Unknown")]
    [InlineData("  Default string  ")]
    [InlineData("  ")]
    [InlineData("")]
    [InlineData(null)]
    public void Oem_placeholder_becomes_null(string? value)
    {
        Assert.Null(OemPlaceholders.Clean(value));
    }

    [Theory]
    [InlineData("MS-7D48", "MS-7D48")]
    [InlineData("  PRO B760M-E DDR4 (MS-7D48) ", "PRO B760M-E DDR4 (MS-7D48)")]
    [InlineData("Micro-Star International Co., Ltd.", "Micro-Star International Co., Ltd.")]
    public void Real_value_is_kept_and_trimmed(string value, string expected)
    {
        Assert.Equal(expected, OemPlaceholders.Clean(value));
    }
}
