namespace Booster.Core.Abstractions;

/// <summary>Уровень безопасности действия (docs/PRINCIPLES.md, «Модель безопасности действий»).</summary>
public enum SafetyLevel
{
    /// <summary>Только чтение.</summary>
    L0ReadOnly = 0,

    /// <summary>Обратимое изменение на время сессии.</summary>
    L1Reversible = 1,

    /// <summary>Изменение только после явного подтверждения пользователя.</summary>
    L2Confirmed = 2,

    /// <summary>Только рекомендация; пользователь выполняет вручную.</summary>
    L3ManualOnly = 3,
}
