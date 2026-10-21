using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Profiles;
using Booster.Core.Resources;

namespace Booster.Core.SystemHealth;

/// <summary>
/// Отключение элемента автозагрузки записью в StartupApproved, как в диспетчере задач. Действует со следующего входа:
/// запущенный процесс не завершается, сам элемент автозагрузки не удаляется. Элемент текущего пользователя меняется в HKCU,
/// элемент для всех пользователей (HKLM Run, Run32, общая папка) — в HKLM и только в Booster с правами администратора.
/// </summary>
public sealed class StartupItemAction : IReversibleAction
{
    private readonly IRegistryValues? _registry;
    private readonly string? _key;
    private readonly StartupEntry _entry;
    private readonly TimeProvider _time;
    private (string Key, UserRegistryValue Value)? _captured;

    public StartupItemAction(IUserRegistry userRegistry, IMachineRegistry machineRegistry, StartupEntry entry, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(userRegistry);
        ArgumentNullException.ThrowIfNull(machineRegistry);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(time);

        _entry = entry;
        _time = time;
        var isMachine = entry.Location is StartupLocation.MachineRun or StartupLocation.MachineRun32 or StartupLocation.CommonFolder;
        _key = entry.Location switch
        {
            StartupLocation.UserRun or StartupLocation.MachineRun => StartupApprovedKeys.Run,
            StartupLocation.MachineRun32 => StartupApprovedKeys.Run32,
            StartupLocation.UserFolder or StartupLocation.CommonFolder => StartupApprovedKeys.StartupFolder,
            // RunOnce выполняется один раз и удаляется самой Windows: отключать в нём нечего.
            _ => null,
        };
        _registry = _key is null ? null : isMachine ? machineRegistry : (IRegistryValues)userRegistry;

        Id = isMachine ? ActionIds.MachineStartupItem : ActionIds.StartupItem;
        Descriptor = new ActionDescriptor(Id, isMachine ? ReportStrings.Action_MachineStartupItem : ReportStrings.Action_StartupItem,
            SafetyLevel.L2Confirmed, RequiresAdmin: isMachine, RequiresReboot: false, RequiresConfirmation: true, IsReversible: true,
            AllowedDuringGaming: false, VerifiedForAutomatic: false);
    }

    public string Id { get; }

    public ActionDescriptor Descriptor { get; }

    public Task<ActionSupport> CheckSupportAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (_, reason) = Evaluate();
        return Task.FromResult(reason is null
            ? new ActionSupport(true, null, string.Format(CultureInfo.CurrentCulture, ReportStrings.StartupItemSummaryFormat, _entry.Name))
            : new ActionSupport(false, reason, Descriptor.DisplayName));
    }

    public Task<ActionSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (original, reason) = Evaluate();
        if (reason is not null)
            throw new InvalidOperationException(reason);

        var now = _time.GetUtcNow();
        var applied = UserRegistryValue.FromBinary(StartupApproval.Disabled(now));
        _captured = (_key!, applied);
        return Task.FromResult(UserRegistrySnapshot.Capture(Id, _key!, _entry.Name, original, applied, now));
    }

    public Task<ApplyResult> ApplyAsync(CancellationToken cancellationToken)
    {
        var (key, value) = _captured ?? throw new InvalidOperationException("CaptureAsync must precede ApplyAsync.");
        cancellationToken.ThrowIfCancellationRequested();
        _registry!.Write(key, _entry.Name, value);
        return Task.FromResult(ApplyResult.Done);
    }

    private (UserRegistryValue? Original, string? Reason) Evaluate()
    {
        if (_registry is null || _key is null)
            return (null, ReportStrings.Skip_StartupItemRunOnce);

        UserRegistryValue? current;
        try
        {
            current = _registry.Read(_key, _entry.Name);
        }
        catch (InvalidDataException)
        {
            return (null, ReportStrings.Skip_StartupItemStateUnknown);
        }

        // Значение не REG_BINARY — формат неизвестен: отключать поверх него нельзя.
        var state = current is null ? StartupApproval.Parse(null)
            : current.Kind == UserRegistryValueKind.Binary ? StartupApproval.Parse(current.ToBytes())
            : StartupState.Unknown;
        return state switch
        {
            StartupState.Disabled => (current, ReportStrings.Skip_StartupItemAlreadyDisabled),
            StartupState.Unknown => (current, ReportStrings.Skip_StartupItemStateUnknown),
            _ => (current, null),
        };
    }
}
