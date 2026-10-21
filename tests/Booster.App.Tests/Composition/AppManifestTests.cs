using System.IO;
using System.Xml.Linq;

namespace Booster.App.Tests.Composition;

/// <summary>Уровень запуска — решение пользователя (docs/ARCHITECTURE.md, раздел 8): тест не даёт сменить его незаметно.</summary>
public sealed class AppManifestTests
{
    private static readonly XNamespace AsmV3 = "urn:schemas-microsoft-com:asm.v3";

    [Fact]
    public void Booster_asks_for_highest_available_rights_without_ui_access()
    {
        var level = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "Booster.App", "app.manifest"))
            .Descendants(AsmV3 + "requestedExecutionLevel").Single();

        Assert.Equal(("highestAvailable", "false"), ((string?)level.Attribute("level"), (string?)level.Attribute("uiAccess")));
    }

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Booster.sln")))
                return dir.FullName;
        }
        throw new InvalidOperationException($"Booster.sln not found above {AppContext.BaseDirectory}");
    }
}
