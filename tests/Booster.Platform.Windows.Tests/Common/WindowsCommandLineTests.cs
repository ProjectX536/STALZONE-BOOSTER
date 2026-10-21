using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Tests.Common;

public class WindowsCommandLineTests
{
    [Theory]
    [InlineData("--timed", "--timed")]
    [InlineData(@"C:\Captures\a.csv", @"C:\Captures\a.csv")]
    [InlineData("My Game.exe", "\"My Game.exe\"")]
    [InlineData("", "\"\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"C:\Program Files\", "\"C:\\Program Files\\\\\"")]
    [InlineData("a\\\\\"b", "\"a\\\\\\\\\\\"b\"")]
    [InlineData("tab\there", "\"tab\there\"")]
    public void Argument_is_quoted_only_when_needed(string argument, string expected) =>
        Assert.Equal(expected, WindowsCommandLine.Quote(argument));

    [Fact]
    public void Arguments_are_joined_with_spaces() =>
        Assert.Equal("--process_name \"My Game.exe\" --timed 60", WindowsCommandLine.Join(["--process_name", "My Game.exe", "--timed", "60"]));
}
