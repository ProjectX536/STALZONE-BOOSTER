using System.Collections.ObjectModel;
using System.Globalization;
using Booster.App.Resources;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Profiles;
using Booster.Core.Reports;
using Booster.Platform.Windows.Monitoring;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Booster.App.ViewModels;

/// <summary>Строка действия профиля, которое Booster умеет выполнять.</summary>
public sealed partial class ProfileActionRow : ObservableObject
{
    public ProfileActionRow(string id, string name, string modeText, string levelText, ActionTexts texts, bool isOptional, bool isEnabled)
    {
        Id = id;
        Name = name;
        ModeText = modeText;
        LevelText = levelText;
        Description = texts.Description;
        Risk = texts.Risk;
        Undo = texts.Undo;
        IsOptional = isOptional;
        IsEnabled = isEnabled;
    }

    public string Id { get; }

    public string Name { get; }

    public string ModeText { get; }

    public string LevelText { get; }

    public string Description { get; }

    public string Risk { get; }

    public string Undo { get; }

    /// <summary>Режим optional: действие выполняется, только если пользователь его включил.</summary>
    public bool IsOptional { get; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    /// <summary>Имя строки для экранного диктора.</summary>
    public override string ToString() => Name;
}

/// <summary>Запись профиля без реализации: Booster только рекомендует.</summary>
public sealed record RecommendationRow(string Id, string Text)
{
    public override string ToString() => Id;
}

/// <summary>Приложение, которое пользователь разрешает закрывать перед игрой.</summary>
public sealed partial class BackgroundAppRow(string name, bool isSelected) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = isSelected;

    public override string ToString() => Name;
}

/// <summary>Описание, риск и способ отмены действия (у L2/L3 — риск и способ отмены).</summary>
public sealed record ActionTexts(string Description, string Risk, string Undo)
{
    public static ActionTexts For(string actionId) => actionId switch
    {
        ActionIds.PowerPlan => new(Strings.PowerPlanDescription, Strings.PowerPlanRisk, Strings.PowerPlanUndo),
        ActionIds.GamePriority => new(Strings.GamePriorityDescription, Strings.GamePriorityRisk, Strings.GamePriorityUndo),
        ActionIds.BackgroundApps => new(Strings.BackgroundAppsDescription, Strings.BackgroundAppsRisk, Strings.BackgroundAppsUndo),
        _ => new(string.Empty, string.Empty, string.Empty),
    };
}

/// <summary>
/// «Игровые профили»: игры профиля, действия и их режимы, фоновые приложения, автозапуск. Сохраняются только выбор
/// пользователя (<see cref="ProfileUserChoices"/>); панель сессии работает с сохранённым профилем.
/// </summary>
public sealed partial class ProfilesViewModel : ObservableObject, IPage
{
    private readonly IProfileRepository _profiles;
    private readonly IActionCatalog _catalog;
    private readonly IProcessListSource _processes;
    private readonly ILogger<ProfilesViewModel> _logger;
    private IReadOnlyList<ProcessEntry> _running = [];

    public ProfilesViewModel(IProfileRepository profiles, IActionCatalog catalog, IProcessListSource processes, SessionViewModel session,
        ILogger<ProfilesViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(session);
        _profiles = profiles;
        _catalog = catalog;
        _processes = processes;
        _logger = logger;
        Session = session;
        Profiles = profiles.Current.Profiles;
        LoadWarnings = WarningLines(profiles.Current.Warnings);
        SelectedProfile = Profiles.FirstOrDefault();
    }

    public SessionViewModel Session { get; }

    [ObservableProperty]
    public partial IReadOnlyList<GamingProfile> Profiles { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial GamingProfile? SelectedProfile { get; set; }

    public ObservableCollection<string> Games { get; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ProcessEntry> RunningProcesses { get; private set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddRunningGameCommand))]
    public partial ProcessEntry? SelectedProcess { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTypedGameCommand))]
    public partial string NewGameName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<ProfileActionRow> Actions { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<RecommendationRow> Recommendations { get; private set; } = [];

    public ObservableCollection<BackgroundAppRow> BackgroundApps { get; } = [];

    [ObservableProperty]
    public partial bool AutoStart { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    /// <summary>Замечания к файлам профилей при последней загрузке: файл не принят или часть значений исправлена.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadWarnings))]
    public partial IReadOnlyList<string> LoadWarnings { get; private set; } = [];

    public bool HasLoadWarnings => LoadWarnings.Count > 0;

    /// <summary>Переход на экран обновляет список запущенных окон; несохранённые правки остаются.</summary>
    public void Activate()
    {
        if (RefreshProcessesCommand.CanExecute(null))
            RefreshProcessesCommand.Execute(null);
    }

    [RelayCommand]
    private async Task RefreshProcessesAsync()
    {
        try
        {
            _running = await Task.Run(_processes.GetWindowedProcesses);
        }
        catch (Exception ex)
        {
            LogProcessListFailed(ex);
            _running = [];
        }
        RunningProcesses = _running;
        RebuildBackgroundApps();
    }

    private bool CanAddRunningGame() => SelectedProcess is not null;

    [RelayCommand(CanExecute = nameof(CanAddRunningGame))]
    private void AddRunningGame()
    {
        if (SelectedProcess is { } process)
            AddGame(process.Name);
    }

    private bool CanAddTypedGame() => ProcessNames.Normalize(NewGameName) is not null;

    [RelayCommand(CanExecute = nameof(CanAddTypedGame))]
    private void AddTypedGame()
    {
        if (AddGame(NewGameName))
            NewGameName = string.Empty;
    }

    [RelayCommand]
    private void RemoveGame(string? name)
    {
        if (name is not null && Games.Remove(name))
            RebuildBackgroundApps();
    }

    private bool CanSave() => SelectedProfile is not null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken ct)
    {
        if (SelectedProfile is not { } profile)
            return;

        var choices = new ProfileUserChoices(
            [.. Games],
            AutoStart,
            [.. Actions.Where(a => a.IsOptional && a.IsEnabled).Select(a => a.Id)],
            [.. BackgroundApps.Where(a => a.IsSelected).Select(a => a.Name)]);
        try
        {
            await Task.Run(() => _profiles.SaveUserChoicesAsync(profile.ProfileId, choices, ct), ct);
            var profileId = profile.ProfileId;
            Profiles = _profiles.Current.Profiles;
            LoadWarnings = WarningLines(_profiles.Current.Warnings);
            SelectedProfile = _profiles.Current.Find(profileId) ?? Profiles.FirstOrDefault();
            StatusMessage = Strings.ProfileSaved;
            LogSaved(profileId, choices.Games.Count, choices.BackgroundApps.Count, choices.AutoStart);
        }
        catch (ProfileFileRejectedException ex)
        {
            LogSaveFailed(ex);
            StatusMessage = string.Format(CultureInfo.CurrentCulture, Strings.ProfileFileRejectedFormat, ex.FileName);
        }
        catch (Exception ex)
        {
            LogSaveFailed(ex);
            StatusMessage = Strings.ProfileSaveFailed;
        }
    }

    partial void OnSelectedProfileChanged(GamingProfile? value)
    {
        Games.Clear();
        foreach (var game in value?.Games ?? [])
            Games.Add(game);
        AutoStart = value?.AutoStart ?? false;
        BuildActions(value);
        BackgroundApps.Clear();
        RebuildBackgroundApps(value?.BackgroundApps.UserSelected ?? []);
        Session.ProfileId = value?.ProfileId;
    }

    private bool AddGame(string? name)
    {
        if (ProcessNames.Normalize(name) is not { } normalized)
            return false;
        if (!Games.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            Games.Add(normalized);
        RebuildBackgroundApps();
        return true;
    }

    private void BuildActions(GamingProfile? profile)
    {
        var actions = new List<ProfileActionRow>();
        var recommendations = new List<RecommendationRow>();
        foreach (var entry in profile?.Actions ?? [])
        {
            // Только конструктор действия: ни проверок, ни изменений.
            if (_catalog.Create(entry.Id, profile!, game: null) is not { } action)
            {
                recommendations.Add(new RecommendationRow(entry.Id, RecommendationText(entry.Id)));
                continue;
            }

            var descriptor = action.Descriptor;
            var enabled = profile!.EnabledOptionalActions.Contains(entry.Id, StringComparer.Ordinal);
            actions.Add(new ProfileActionRow(entry.Id, descriptor.DisplayName, EnumText.Format(entry.Mode), EnumText.Format(descriptor.Level),
                ActionTexts.For(entry.Id), entry.Mode == ActionMode.Optional, enabled));
        }
        Actions = actions;
        Recommendations = recommendations;
    }

    /// <summary>
    /// Ошибка чтения или разбора, как и у <see cref="ProfileRepository"/>, значит, что пользовательский файл не применён целиком;
    /// исправленные и удалённые значения — что остальной файл действует.
    /// </summary>
    private static List<string> WarningLines(IReadOnlyList<ConfigWarning> warnings) =>
        [.. warnings.Select(w => w.Kind is ConfigWarningKind.ValueClamped or ConfigWarningKind.InvalidValue
            ? string.Format(CultureInfo.CurrentCulture, Strings.ConfigWarningFormat, w.File, EnumText.Format(w.Kind), w.Detail)
            : string.Format(CultureInfo.CurrentCulture, Strings.ProfileFileNotAppliedFormat, w.File, w.Detail))];

    private static string RecommendationText(string actionId) => actionId switch
    {
        "registry-tweaks" => Strings.RecommendRegistryTweaks,
        "driver-updates" => Strings.RecommendDriverUpdates,
        "pagefile" => Strings.RecommendPagefile,
        "windows-services" => Strings.RecommendWindowsServices,
        _ => Strings.RecommendOther,
    };

    /// <summary>
    /// Запущенные окна, кроме защищённых процессов и игр профиля, плюс уже выбранные — даже если они не запущены.
    /// Отметки сохраняются.
    /// </summary>
    private void RebuildBackgroundApps(IEnumerable<string>? selected = null)
    {
        var chosen = new HashSet<string>(selected ?? BackgroundApps.Where(a => a.IsSelected).Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
        var games = new HashSet<string>(Games, StringComparer.OrdinalIgnoreCase);
        var names = _running.Select(p => ProcessNames.Normalize(p.Name))
            .OfType<string>()
            .Where(n => !ProtectedProcesses.IsProtected(n) && !games.Contains(n))
            .Concat(chosen.Where(n => !games.Contains(n)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);

        BackgroundApps.Clear();
        foreach (var name in names)
            BackgroundApps.Add(new BackgroundAppRow(name, chosen.Contains(name)));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window list for the profile screen failed")]
    private partial void LogProcessListFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Profile {ProfileId} saved: {Games} games, {Apps} background apps, autostart {AutoStart}")]
    private partial void LogSaved(string profileId, int games, int apps, bool autoStart);

    [LoggerMessage(Level = LogLevel.Error, Message = "Profile save failed")]
    private partial void LogSaveFailed(Exception exception);
}
