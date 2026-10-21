using System.Diagnostics;
using System.Text.RegularExpressions;
using Booster.Platform.Windows.Providers;

namespace Booster.Platform.Windows.Tests.Providers;

[Trait("Category", "Host")]
public partial class PowerPlanProviderHostTests
{
    private readonly PowerPlanProvider _provider = new();

    [Fact]
    public void Schemes_have_unique_ids_and_names()
    {
        var schemes = _provider.ListSchemes();

        Assert.NotEmpty(schemes);
        Assert.Equal(schemes.Count, schemes.Select(s => s.Id).Distinct().Count());
        Assert.All(schemes, s => Assert.False(string.IsNullOrWhiteSpace(s.Name)));
    }

    [Fact]
    public void Active_scheme_is_listed_and_matches_powercfg()
    {
        var active = _provider.GetActiveScheme();

        Assert.Contains(_provider.ListSchemes(), s => s.Id == active);
        Assert.Equal(PowercfgActiveScheme(), active);
    }

    [Fact]
    public void Power_source_is_read()
    {
        Assert.True(Enum.IsDefined(_provider.GetPowerSource()));
    }

    private static Guid PowercfgActiveScheme()
    {
        using var process = Process.Start(new ProcessStartInfo("powercfg.exe", "/getactivescheme")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(0, process.ExitCode);
        return Guid.Parse(GuidPattern().Match(output).Value);
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();
}
