using Booster.Core.Recovery;

namespace Booster.Core.Abstractions;

/// <summary>Возврат по снимку из журнала. Экземпляр действия не нужен: после сбоя его нет.</summary>
public interface IActionRestorer
{
    string ActionId { get; }

    /// <summary>Старшая понятная версия <see cref="ActionSnapshot"/>.</summary>
    int SnapshotSchemaVersion { get; }

    /// <summary>Только чтение: в каком состоянии сейчас то, что меняло действие.</summary>
    Task<ObservedState> InspectAsync(ActionSnapshot snapshot, CancellationToken ct);

    /// <summary>Вернуть <see cref="ActionSnapshot.OriginalValue"/>; вызывается только после <see cref="InspectAsync"/> == <see cref="ObservedState.Applied"/>.</summary>
    Task RevertAsync(ActionSnapshot snapshot, CancellationToken ct);
}
