namespace Booster.Core.Abstractions;

/// <summary>
/// Неизменяемое описание действия (docs/ARCHITECTURE.md, раздел 4).
/// <see cref="VerifiedForAutomatic"/> задаётся в коде действия, а не в конфигурации:
/// только проверенные L1-действия выполняются автоматически.
/// </summary>
public sealed record ActionDescriptor(
    string Id,
    string DisplayName,
    SafetyLevel Level,
    bool RequiresAdmin,
    bool RequiresReboot,
    bool RequiresConfirmation,
    bool IsReversible,
    bool AllowedDuringGaming,
    bool VerifiedForAutomatic);
