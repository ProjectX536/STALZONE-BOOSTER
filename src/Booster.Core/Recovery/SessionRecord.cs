using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Booster.Core.Abstractions;
using Booster.Core.Engine;

namespace Booster.Core.Recovery;

/// <summary>Одно изменение сессии.</summary>
/// <param name="Summary">Что меняется, текстом для журнала: «Сбалансированная → Высокая производительность».</param>
/// <param name="Result">Фактический результат применения (например, какие приложения закрылись).</param>
/// <param name="Verified">Итог проверки после восстановления; null — не проверялось.</param>
public sealed record ChangeEntry(
    string ActionId,
    string DisplayName,
    string Summary,
    bool IsReversible,
    bool RequiresReboot,
    ChangeStatus Status,
    ActionSnapshot Snapshot,
    JsonElement? Result,
    string? Detail,
    bool? Verified,
    DateTimeOffset UpdatedAt);

public sealed record SkippedAction(string ActionId, string DisplayName, string Reason);

/// <summary>Файл журнала одной игровой сессии или подготовки к ней (docs/ARCHITECTURE.md, раздел 7).</summary>
/// <param name="GameName">У подготовки — игры профиля через запятую: сама игра ещё не запущена.</param>
/// <param name="Kind">Необязательное поле: в журналах Этапа 2 его нет, такая запись — игровая сессия. Версия схемы не меняется.</param>
public sealed record SessionRecord(
    int SchemaVersion,
    string SessionId,
    string ProfileId,
    string ProfileName,
    string GameName,
    int? GameProcessId,
    ExecutionTrigger Trigger,
    DateTimeOffset StartedAt,
    SessionState State,
    SessionOutcome? Outcome,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<ChangeEntry> Changes,
    IReadOnlyList<SkippedAction> Skipped,
    SessionKind Kind = SessionKind.Game)
{
    public const int CurrentSchemaVersion = 1;

    public bool IsFinished => Outcome is not null;
}

/// <summary>Идентификатор сессии — он же имя файла журнала: <c>yyyyMMdd-HHmmss-xxxxxxxx</c> (время UTC и 8 hex).</summary>
public static partial class SessionIds
{
    public static string New(DateTimeOffset now) =>
        now.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-"
        + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));

    public static bool IsValid(string? sessionId) => sessionId is not null && Pattern().IsMatch(sessionId);

    [GeneratedRegex("^[0-9]{8}-[0-9]{6}-[0-9a-f]{8}$")]
    private static partial Regex Pattern();
}
