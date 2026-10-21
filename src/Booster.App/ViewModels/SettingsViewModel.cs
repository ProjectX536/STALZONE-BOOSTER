using System.Globalization;
using System.IO;
using Booster.App.Resources;
using Booster.App.Services;
using Booster.Core.Configuration;
using Booster.Core.Reports;
using Booster.Platform.Windows.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Booster.App.ViewModels;

/// <summary>
/// Настройки PresentMon и бенчмарка: путь к exe и длительность прогона по умолчанию. Сохраняются в пользовательский settings.json;
/// изменённый путь действует сразу — поиск PresentMon читает его при каждом замере.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IPage
{
    private readonly IBoosterConfiguration _config;
    private readonly FilePicker _picker;
    private readonly ILogger<SettingsViewModel> _logger;

    public SettingsViewModel(IBoosterConfiguration config, FilePicker picker, IAppPaths paths, ILogger<SettingsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(paths);
        _config = config;
        _picker = picker;
        _logger = logger;
        SettingsFileText = string.Format(CultureInfo.CurrentCulture, Strings.SettingsFile, Path.Combine(paths.UserConfig, ConfigFiles.Settings));
        Load();
    }

    /// <summary>Пусто — PresentMon ищется в папке инструментов Booster и рядом с Booster.exe.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string PresentMonPath { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string DurationText { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    /// <summary>Замечания к конфигурации: исправленные значения, нечитаемые файлы.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Warnings { get; private set; } = [];

    public string SettingsFileText { get; }

    /// <summary>Переход на экран не сбрасывает несохранённые правки; обновляются только замечания.</summary>
    public void Activate() => Warnings = FormatWarnings(_config.Current.Warnings);

    [RelayCommand]
    private void Browse()
    {
        if (_picker.PickExecutable(PresentMonPath) is { } path)
            PresentMonPath = path;
    }

    private bool CanSave() => TryGetDuration(out _);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken ct)
    {
        var path = string.IsNullOrWhiteSpace(PresentMonPath) ? null : PresentMonPath.Trim().Trim('"');
        if (path is not null && !File.Exists(path))
        {
            StatusMessage = string.Format(CultureInfo.CurrentCulture, Strings.FileNotFound, path);
            return;
        }
        if (!TryGetDuration(out var seconds))
            return;

        var settings = _config.Current.Settings;
        var updated = settings with
        {
            PresentMon = settings.PresentMon with { ExecutablePath = path },
            Benchmark = settings.Benchmark with { RunSeconds = seconds },
        };
        try
        {
            await Task.Run(() => _config.SaveUserSettingsAsync(updated, ct), ct);
            Load();
            StatusMessage = Strings.SettingsSaved;
            LogSaved(path ?? "(auto)", seconds);
        }
        catch (Exception ex)
        {
            LogSaveFailed(ex);
            StatusMessage = Strings.SettingsSaveFailed;
        }
    }

    private bool TryGetDuration(out int seconds) =>
        int.TryParse(DurationText, NumberStyles.Integer, CultureInfo.CurrentCulture, out seconds)
        && seconds is >= SettingsNormalizer.MinimumRunSeconds and <= SettingsNormalizer.MaximumRunSeconds;

    private void Load()
    {
        var current = _config.Current;
        PresentMonPath = current.Settings.PresentMon.ExecutablePath ?? string.Empty;
        DurationText = current.Settings.Benchmark.RunSeconds.ToString(CultureInfo.CurrentCulture);
        Warnings = FormatWarnings(current.Warnings);
    }

    private static List<string> FormatWarnings(IReadOnlyList<ConfigWarning> warnings) =>
        warnings.Select(w => string.Format(CultureInfo.CurrentCulture, Strings.ConfigWarningFormat, w.File, EnumText.Format(w.Kind), w.Detail))
            .ToList();

    [LoggerMessage(Level = LogLevel.Information, Message = "Settings saved: PresentMon path {Path}, benchmark run {Seconds} s")]
    private partial void LogSaved(string path, int seconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Settings save failed")]
    private partial void LogSaveFailed(Exception exception);
}
