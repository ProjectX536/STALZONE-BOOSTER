using Booster.App.Services;

namespace Booster.App.Tests.TestSupport;

internal sealed class FakeSettingsOpener : ISettingsOpener
{
    public List<string> Pages { get; } = [];

    public List<string> Revealed { get; } = [];

    public bool OpenResult { get; set; } = true;

    public bool RevealResult { get; set; } = true;

    public bool TryOpenSettings(string page)
    {
        Pages.Add(page);
        return OpenResult;
    }

    public bool RevealFile(string path)
    {
        Revealed.Add(path);
        return RevealResult;
    }
}
