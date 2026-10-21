namespace Booster.Core.Recovery;

/// <summary>Статус изменения в журнале (docs/PRINCIPLES.md, «Модель безопасности действий»).</summary>
public enum ChangeStatus
{
    /// <summary>Исходное значение сохранено, успех применения не зафиксирован.</summary>
    Pending,
    Applied,
    Restored,

    /// <summary>Значение изменил пользователь или другая программа; Booster его не трогает.</summary>
    RestoreConflict,
    RestoreFailed,
    NeedsReboot,
    NeedsManualCheck,
}

/// <summary>Текущее состояние изменённого объекта относительно снимка.</summary>
public enum ObservedState
{
    Original,
    Applied,
    Other,

    /// <summary>Объекта больше нет (процесс завершён) — возвращать нечего.</summary>
    Gone,
}

/// <summary>Состояния автомата сессии (docs/ARCHITECTURE.md, раздел 6).</summary>
public enum SessionState
{
    Idle,
    Preflight,
    Snapshot,
    Apply,
    Gaming,
    Restore,
    Verify,
    Recovery,
}

public enum SessionOutcome
{
    Completed,

    /// <summary>Сбой Apply: применённое откатано.</summary>
    RolledBack,

    /// <summary>Сессию завершило восстановление при старте после сбоя.</summary>
    Recovered,
}

public enum SessionKind
{
    /// <summary>Игровая сессия: обратимые действия на время игры.</summary>
    Game,

    /// <summary>Подготовка до запуска игры: только подтверждённые необратимые действия (закрытие фоновых приложений).</summary>
    Preparation,

    /// <summary>
    /// Постоянное изменение по кнопке пользователя: остаётся Applied после завершения сессии, пока его не вернут
    /// с экрана журнала. Профиля у такой сессии нет.
    /// </summary>
    Maintenance,
}
