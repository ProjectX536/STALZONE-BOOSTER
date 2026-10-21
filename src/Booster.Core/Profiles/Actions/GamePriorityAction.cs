using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Booster.Core.Abstractions;
using Booster.Core.Reports;
using Booster.Core.Resources;

namespace Booster.Core.Profiles.Actions;

/// <summary>
/// Повышение приоритета процесса игры. Эффект живёт не дольше процесса, восстановление проверено тестами,
/// поэтому <see cref="ActionDescriptor.VerifiedForAutomatic"/> = true. Приоритет только повышается.
/// </summary>
/// <param name="game">null — план без игры (предпросмотр): поддержка проверяется при запуске.</param>
public sealed class GamePriorityAction(IProcessPriorityController priorities, GameProcess? game, ProcessPriority target, TimeProvider time)
    : IReversibleAction
{
    private GameProcess? _captured;

    public string Id => ActionIds.GamePriority;

    public ActionDescriptor Descriptor { get; } = new(ActionIds.GamePriority, ReportStrings.Action_GamePriority, SafetyLevel.L1Reversible,
        RequiresAdmin: false, RequiresReboot: false, RequiresConfirmation: false, IsReversible: true,
        AllowedDuringGaming: true, VerifiedForAutomatic: true);

    public Task<ActionSupport> CheckSupportAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (game is null)
            return Task.FromResult(new ActionSupport(true, null, Format(ReportStrings.GamePriorityPendingSummaryFormat, EnumText.Format(target))));

        var (current, reason) = Evaluate(game);
        return Task.FromResult(reason is null
            ? new ActionSupport(true, null, Format(ReportStrings.GamePrioritySummaryFormat, game.Name, EnumText.Format(current!.Value),
                EnumText.Format(target)))
            : new ActionSupport(false, reason, ReportStrings.Action_GamePriority));
    }

    public Task<ActionSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (game is null)
            throw new InvalidOperationException("Game priority needs a running game.");
        var (current, reason) = Evaluate(game);
        if (reason is not null)
            throw new InvalidOperationException(reason);

        _captured = game;
        return Task.FromResult(new ActionSnapshot(GamePriorityValue.SchemaVersion, Id, GamePriorityValue.Write(game, current!.Value),
            GamePriorityValue.Write(game, target), time.GetUtcNow()));
    }

    public Task<ApplyResult> ApplyAsync(CancellationToken cancellationToken)
    {
        var captured = _captured ?? throw new InvalidOperationException("CaptureAsync must precede ApplyAsync.");
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            priorities.SetPriority(captured.Identity, target);
        }
        catch (UnauthorizedAccessException)
        {
            // Доступ был при проверке, но пропал (античит снял права): приоритет не изменён, откатывать сессию не за что.
            return Task.FromResult(ApplyResult.NotApplied(ReportStrings.Skip_NoAccessToGame));
        }

        return Task.FromResult(ApplyResult.Done);
    }

    private (ProcessPriority? Current, string? Reason) Evaluate(GameProcess running)
    {
        if (priorities.GetPriority(running.Identity) is not { } current)
            return (null, ReportStrings.Skip_GameUnavailable);
        if (!priorities.CanSetPriority(running.Identity))
            return (current, ReportStrings.Skip_NoAccessToGame);
        return current >= target ? (current, Format(ReportStrings.Skip_PriorityAlreadyFormat, EnumText.Format(current))) : (current, null);
    }

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}

/// <summary>Значение снимка <c>game-priority</c>: <c>{"processId", "startTime", "name", "priority"}</c>.</summary>
internal sealed record GamePriorityValue(
    [property: JsonPropertyName("processId")] int ProcessId,
    [property: JsonPropertyName("startTime")] DateTimeOffset StartTime,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("priority"), JsonConverter(typeof(JsonStringEnumConverter<ProcessPriority>))] ProcessPriority Priority)
{
    public const int SchemaVersion = 1;

    public ProcessIdentity Identity => new(ProcessId, StartTime);

    public static JsonElement Write(GameProcess game, ProcessPriority priority) =>
        JsonSerializer.SerializeToElement(new GamePriorityValue(game.Identity.ProcessId, game.Identity.StartTime, game.Name, priority));

    public static GamePriorityValue Read(JsonElement value) =>
        value.Deserialize<GamePriorityValue>() ?? throw new JsonException("game-priority snapshot value is null");
}
