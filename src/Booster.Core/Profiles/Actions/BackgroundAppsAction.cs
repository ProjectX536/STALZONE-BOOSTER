using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Booster.Core.Abstractions;
using Booster.Core.Resources;

namespace Booster.Core.Profiles.Actions;

/// <summary>
/// Штатное закрытие фоновых приложений, выбранных пользователем. Закрытые приложения не перезапускаются, поэтому действие
/// необратимо (L2): восстановителя нет, после подготовки запись получает <c>NeedsManualCheck</c>.
/// Выполняется только при подготовке сессии, до запуска игры: в игровую сессию необратимое не попадает.
/// Закрываются только приложения из <see cref="BackgroundAppsSettings.UserSelected"/>, кроме защищённых процессов и игр профиля.
/// </summary>
/// <param name="game">null — подготовка до запуска игры или её предпросмотр.</param>
public sealed class BackgroundAppsAction(IProcessProvider processes, IAppCloser closer, GamingProfile profile, GameProcess? game,
    TimeProvider time) : IReversibleAction
{
    private const string NoApps = "—";

    private IReadOnlyList<string>? _captured;

    public string Id => ActionIds.BackgroundApps;

    public ActionDescriptor Descriptor { get; } = new(ActionIds.BackgroundApps, ReportStrings.Action_BackgroundApps, SafetyLevel.L2Confirmed,
        RequiresAdmin: false, RequiresReboot: false, RequiresConfirmation: true, IsReversible: false,
        AllowedDuringGaming: false, VerifiedForAutomatic: false);

    public Task<ActionSupport> CheckSupportAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var candidates = Candidates();
        if (candidates.Count == 0)
            return Task.FromResult(new ActionSupport(false, ReportStrings.Skip_NoAppsSelected, ReportStrings.Action_BackgroundApps));

        var running = Running(candidates);
        return Task.FromResult(running.Count == 0
            ? new ActionSupport(false, ReportStrings.Skip_NoSelectedAppRunning, ReportStrings.Action_BackgroundApps)
            : new ActionSupport(true, null, Format(ReportStrings.BackgroundAppsSummaryFormat, List(running))));
    }

    public Task<ActionSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var candidates = Candidates();
        if (candidates.Count == 0)
            throw new InvalidOperationException(ReportStrings.Skip_NoAppsSelected);
        var running = Running(candidates);
        if (running.Count == 0)
            throw new InvalidOperationException(ReportStrings.Skip_NoSelectedAppRunning);

        _captured = running;
        var value = BackgroundAppsValue.Write(running);
        return Task.FromResult(new ActionSnapshot(BackgroundAppsValue.SchemaVersion, Id, value, value, time.GetUtcNow()));
    }

    public async Task<ApplyResult> ApplyAsync(CancellationToken cancellationToken)
    {
        var apps = _captured ?? throw new InvalidOperationException("CaptureAsync must precede ApplyAsync.");
        cancellationToken.ThrowIfCancellationRequested();
        var timeout = TimeSpan.FromMilliseconds(profile.BackgroundApps.CloseTimeoutMs);
        var outcome = new BackgroundAppsOutcome([], [], [], [], []);
        foreach (var app in apps)
        {
            CloseRequestResult result;
            try
            {
                result = await closer.RequestCloseAsync(app, timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Сбой по одному приложению не мешает закрыть остальные; пробрасывается только отмена.
                outcome.Failed.Add(app);
                continue;
            }

            (result switch
            {
                CloseRequestResult.Closed => outcome.Closed,
                CloseRequestResult.StillRunning => outcome.StillRunning,
                CloseRequestResult.NoWindow => outcome.NoWindow,
                _ => outcome.NotRunning,
            }).Add(app);
        }

        var notClosed = outcome.StillRunning.Concat(outcome.NoWindow).Concat(outcome.Failed).ToArray();
        return new ApplyResult(JsonSerializer.SerializeToElement(outcome),
            Format(ReportStrings.BackgroundAppsDetailFormat, List(outcome.Closed), List(notClosed)));
    }

    /// <summary>Выбор пользователя без защищённых процессов и игр, в исходном порядке, без повторов.</summary>
    private List<string> Candidates()
    {
        var excluded = profile.Games.Append(game?.Name).Select(ProcessNames.Normalize).OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return profile.BackgroundApps.UserSelected.Select(ProcessNames.Normalize).OfType<string>()
            .Where(name => !ProtectedProcesses.IsProtected(name) && !excluded.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private List<string> Running(List<string> candidates)
    {
        var running = processes.List().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates.Where(running.Contains).ToList();
    }

    private static string List(IReadOnlyCollection<string> apps) => apps.Count == 0 ? NoApps : string.Join(", ", apps);

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}

/// <summary>Значение снимка <c>user-selected-background-apps</c>: <c>{"apps": [...]}</c> — что Booster собирался закрыть.</summary>
internal sealed record BackgroundAppsValue([property: JsonPropertyName("apps")] IReadOnlyList<string> Apps)
{
    public const int SchemaVersion = 1;

    public static JsonElement Write(IReadOnlyList<string> apps) => JsonSerializer.SerializeToElement(new BackgroundAppsValue(apps));
}

/// <summary>Итог закрытия для журнала (<see cref="ApplyResult.Data"/>).</summary>
internal sealed record BackgroundAppsOutcome(
    [property: JsonPropertyName("closed")] List<string> Closed,
    [property: JsonPropertyName("stillRunning")] List<string> StillRunning,
    [property: JsonPropertyName("noWindow")] List<string> NoWindow,
    [property: JsonPropertyName("failed")] List<string> Failed,
    [property: JsonPropertyName("notRunning")] List<string> NotRunning);
