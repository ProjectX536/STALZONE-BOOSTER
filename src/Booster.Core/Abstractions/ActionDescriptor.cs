namespace Booster.Core.Abstractions;

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
