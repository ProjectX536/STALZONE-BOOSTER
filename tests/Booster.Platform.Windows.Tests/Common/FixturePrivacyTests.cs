using System.Text.Json;
using Booster.Platform.Windows.Tests.TestSupport;

namespace Booster.Platform.Windows.Tests.Common;

/// <summary>Фикстуры коммитятся в репозиторий, поэтому в них не должно быть идентификаторов машины и людей.</summary>
public class FixturePrivacyTests
{
    private static readonly HashSet<string> ForbiddenProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "SerialNumber", "UUID", "IdentifyingNumber", "ProcessorId", "UserName", "CSName", "DNSHostName",
        "PrimaryOwnerName", "RegisteredUser", "Organization", "Domain", "Workgroup", "SMBIOSAssetTag",
        "UniqueId", "ObjectId", "Guid", "AccessPaths", "VolumeSerialNumber", "VolumeName",
    };

    public static TheoryData<string> FixtureFiles =>
        [.. Directory.EnumerateFiles(WmiFixture.Root, "*.json", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(WmiFixture.Root, f))];

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void Fixture_has_no_identifying_properties(string relativePath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(WmiFixture.Root, relativePath)));

        var names = doc.RootElement.EnumerateArray().SelectMany(r => r.EnumerateObject()).Select(p => p.Name);

        Assert.DoesNotContain(names, ForbiddenProperties.Contains);
    }

    /// <summary>Все фикстуры: JSON из WMI и CSV из PresentMon.</summary>
    public static TheoryData<string> AllFixtureFiles =>
        [.. Directory.EnumerateFiles(FixturesRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(FixturesRoot, f))];

    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [Theory]
    [MemberData(nameof(AllFixtureFiles))]
    public void Fixture_does_not_mention_this_machine_or_user(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(FixturesRoot, relativePath));

        foreach (var name in new[] { Environment.MachineName, Environment.UserName }.Where(n => n.Length >= 3))
            Assert.DoesNotContain(name, text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void Device_instance_ids_carry_no_device_provided_serial(string relativePath)
    {
        foreach (var row in WmiFixture.Load(relativePath))
        {
            foreach (var id in new[] { row.GetString("DeviceID"), row.GetString("PNPDeviceID") }.OfType<string>())
            {
                var parts = id.Split('\\');
                if (parts.Length < 3 || parts[0].Equals("ROOT", StringComparison.OrdinalIgnoreCase))
                    continue;
                Assert.True(parts[^1].Contains('&') || parts[^1] == "SANITIZED", $"{relativePath}: {id}");
            }
        }
    }
}
