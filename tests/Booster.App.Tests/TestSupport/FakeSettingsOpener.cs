using Booster.App.Services;

namespace Booster.App.Tests.TestSupport;

/// <summary>«Параметры» и Проводник из теста: запоминает, что просили открыть, ничего не запускает.</summary>
internal sealed class FakeSettingsOpener : ISettingsOpener
{
    public List<string> Pages { get; } = [];

    public List<string> Revealed { get; } = [];

    /// <summary>Что вернёт <see cref="TryOpenSettings"/>: false — «Параметры» не открылись.</summary>
    public bool OpenResult { get; set; } = true;

    /// <summary>Что вернёт <see cref="RevealFile"/>: false — Проводник не открылся.</summary>
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
