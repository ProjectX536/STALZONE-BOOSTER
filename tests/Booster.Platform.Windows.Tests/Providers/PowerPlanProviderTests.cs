using System.ComponentModel;
using Booster.Platform.Windows.Providers;

namespace Booster.Platform.Windows.Tests.Providers;

/// <summary>Без системы: чтение имени подменяется.</summary>
public class PowerPlanProviderTests
{
    private static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");

    [Fact]
    public void Readable_name_is_used()
    {
        Assert.Equal("Сбалансированная", PowerSchemeNames.Resolve(Balanced, _ => "Сбалансированная"));
    }

    [Fact]
    public void Unreadable_name_falls_back_to_the_guid()
    {
        // Одна схема с испорченным именем не должна прятать остальные схемы списка.
        Assert.Equal(Balanced.ToString(), PowerSchemeNames.Resolve(Balanced, _ => throw new Win32Exception(2)));
    }

    [Fact]
    public void Empty_name_falls_back_to_the_guid()
    {
        Assert.Equal(Balanced.ToString(), PowerSchemeNames.Resolve(Balanced, _ => "  "));
    }
}
