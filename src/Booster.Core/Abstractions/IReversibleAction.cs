using System.Text.Json;

namespace Booster.Core.Abstractions;

/// <summary>
/// Обратимое действие (docs/ARCHITECTURE.md, раздел 4). Выполняется только через <c>ActionExecutor</c>.
/// Отката здесь нет: после сбоя экземпляра действия уже нет, поэтому возврат по снимку из журнала делает
/// <see cref="IActionRestorer"/> (docs/ARCHITECTURE.md, раздел 4).
/// </summary>
public interface IReversibleAction
{
    string Id { get; }

    ActionDescriptor Descriptor { get; }

    /// <summary>Поддерживается ли и нужно ли сейчас; false — действие пропускается с Reason. Ничего не меняет.</summary>
    Task<ActionSupport> CheckSupportAsync(CancellationToken cancellationToken);

    /// <summary>Исходное значение и значение, которое установит <see cref="ApplyAsync"/>. Ничего не меняет.</summary>
    Task<ActionSnapshot> CaptureAsync(CancellationToken cancellationToken);

    /// <summary>Применяет AppliedValue последнего <see cref="CaptureAsync"/>.</summary>
    Task<ApplyResult> ApplyAsync(CancellationToken cancellationToken);
}

/// <param name="Summary">Что именно изменится: для экрана подтверждения и журнала.</param>
public sealed record ActionSupport(bool IsSupported, string? Reason, string Summary);

/// <param name="Data">Подробности для журнала (например, что закрыто).</param>
/// <param name="Detail">Пояснение для человека; у <see cref="NotApplied"/> — причина пропуска.</param>
public sealed record ApplyResult(JsonElement? Data, string? Detail)
{
    public static ApplyResult Done { get; } = new(null, null);

    /// <summary>false — система не изменилась (например, отказано в доступе): восстанавливать нечего.</summary>
    public bool Applied { get; init; } = true;

    /// <summary>Применить не удалось, но ничего не изменено: действие пропускается, сессия продолжается без отката.</summary>
    public static ApplyResult NotApplied(string reason) => new(null, reason) { Applied = false };
}

/// <summary>Исходное и применённое значение, записываемые в журнал до изменения.</summary>
public sealed record ActionSnapshot(
    int SchemaVersion,
    string ActionId,
    JsonElement OriginalValue,
    JsonElement? AppliedValue,
    DateTimeOffset CapturedAt);
