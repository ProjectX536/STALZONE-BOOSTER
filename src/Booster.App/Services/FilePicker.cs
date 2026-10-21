using System.Globalization;
using System.IO;
using System.Windows;
using Booster.App.Resources;
using Microsoft.Win32;

namespace Booster.App.Services;

public interface IGameExecutablePicker
{
    string? Pick(string expectedFileName);
}

public sealed class FilePicker : IGameExecutablePicker
{
    public string? PickExecutable(string? current)
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.PickPresentMonTitle,
            Filter = Strings.ExecutableFilter,
            CheckFileExists = true,
        };
        if (!string.IsNullOrWhiteSpace(current) && Path.GetDirectoryName(current) is { Length: > 0 } directory && Directory.Exists(directory))
            dialog.InitialDirectory = directory;

        return Show(dialog);
    }

    public string? PickGameExecutable(string expectedFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFileName);
        var dialog = new OpenFileDialog
        {
            Title = string.Format(CultureInfo.CurrentCulture, Strings.PickGameExecutableTitleFormat, expectedFileName),
            Filter = $"{expectedFileName}|{expectedFileName}",
            FileName = expectedFileName,
            CheckFileExists = true,
        };
        return Show(dialog);
    }

    string? IGameExecutablePicker.Pick(string expectedFileName) => PickGameExecutable(expectedFileName);

    private static string? Show(OpenFileDialog dialog)
    {
        var owner = Application.Current?.MainWindow;
        var picked = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return picked == true ? dialog.FileName : null;
    }
}
