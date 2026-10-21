using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Profiles;
using Booster.Core.Resources;

namespace Booster.Core.SystemHealth;

/// <summary>
/// Отключение элемента автозагрузки HKCU записью в StartupApproved, как в диспетчере задач. Действует со следующего входа:
/// запущенный процесс не завершается, сам элемент автозагрузки не удаляется.
/// </summary>
public sealed class StartupItemAction(IUserRegistry registry, StartupEntry entry, TimeProvider time) : IReversibleAction
{
    private (string Key, UserRegistryValue Value)? _captured;

    public string Id => ActionIds.StartupItem;

    public ActionDescriptor Descriptor { get; } = new(ActionIds.StartupItem, ReportStrings.Action_StartupItem, SafetyLevel.L2Confirmed,
        RequiresAdmin: false, RequiresReboot: false, RequiresConfirmation: true, IsReversible: true,
        AllowedDuringGaming: false, VerifiedForAutomatic: false);

    public Task<ActionSupport> CheckSupportAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (_, _, reason) = Evaluate();
        return Task.FromResult(reason is null
            ? new ActionSupport(true, null, string.Format(CultureInfo.CurrentCulture, ReportStrings.StartupItemSummaryFormat, entry.Name))
            : new ActionSupport(false, reason, ReportStrings.Action_StartupItem));
    }

    public Task<ActionSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (key, original, reason) = Evaluate();
        if (reason is not null)
            throw new InvalidOperationException(reason);

        var now = time.GetUtcNow();
        var applied = UserRegistryValue.FromBinary(StartupApproval.Disabled(now));
        _captured = (key!, applied);
        return Task.FromResult(UserRegistrySnapshot.Capture(Id, key!, entry.Name, original, applied, now));
    }

    public Task<ApplyResult> ApplyAsync(CancellationToken cancellationToken)
    {
        var (key, value) = _captured ?? throw new InvalidOperationException("CaptureAsync must precede ApplyAsync.");
        cancellationToken.ThrowIfCancellationRequested();
        registry.Write(key, entry.Name, value);
        return Task.FromResult(ApplyResult.Done);
    }

    private (string? Key, UserRegistryValue? Original, string? Reason) Evaluate()
    {
        var key = ApprovedKey(entry.Location);
        if (key is null)
            return (null, null, ReportStrings.Skip_StartupItemNotUser);

        UserRegistryValue? current;
        try
        {
            current = registry.Read(key, entry.Name);
        }
        catch (InvalidDataException)
        {
            return (key, null, ReportStrings.Skip_StartupItemStateUnknown);
        }

        // Значение не REG_BINARY — формат неизвестен: отключать поверх него нельзя.
        var state = current is null ? StartupApproval.Parse(null)
            : current.Kind == UserRegistryValueKind.Binary ? StartupApproval.Parse(current.ToBytes())
            : StartupState.Unknown;
        return state switch
        {
            StartupState.Disabled => (key, current, ReportStrings.Skip_StartupItemAlreadyDisabled),
            StartupState.Unknown => (key, current, ReportStrings.Skip_StartupItemStateUnknown),
            _ => (key, current, null),
        };
    }

    // HKLM, общая папка и RunOnce — только рекомендация: их состояние хранится вне HKCU или не отключается вовсе.
    private static string? ApprovedKey(StartupLocation location) => location switch
    {
        StartupLocation.UserRun => StartupApprovedKeys.Run,
        StartupLocation.UserFolder => StartupApprovedKeys.StartupFolder,
        _ => null,
    };
}
