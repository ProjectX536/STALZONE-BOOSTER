using System.Globalization;
using System.IO;
using System.Windows;
using Booster.App.Resources;
using Microsoft.Win32;

namespace Booster.App.Services;

/// <summary>Выбор exe игры для настройки, которой нужен путь к файлу.</summary>
public interface IGameExecutablePicker
{
    /// <summary>Полный путь к выбранному файлу или null, если выбор отменён. Имя файла проверяет вызывающий.</summary>
    string? Pick(string expectedFileName);
}

/// <summary>Стандартный диалог выбора файла; отдельно от ViewModel, чтобы та не создавала окна.</summary>
public sealed class FilePicker : IGameExecutablePicker
{
    /// <summary>Путь к выбранному exe или null, если выбор отменён.</summary>
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

    /// <summary>Диалог показывает только файлы с именем <paramref name="expectedFileName"/>; путь можно ввести и вручную.</summary>
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
