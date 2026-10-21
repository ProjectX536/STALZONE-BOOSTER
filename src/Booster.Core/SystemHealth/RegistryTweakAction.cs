using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Profiles;
using Booster.Core.Reports;
using Booster.Core.Resources;

namespace Booster.Core.SystemHealth;

/// <summary>
/// Настройка белого списка реестра HKCU. Запись с именем значения — DWORD <see cref="RegistryTweakDefinition.Value"/>;
/// без имени — пара в строке UserGpuPreferences для exe игры: остальные пары строки сохраняются.
/// </summary>
public sealed class RegistryTweakAction : IReversibleAction
{
    private readonly IUserRegistry _registry;
    private readonly RegistryTweakDefinition _definition;
    private readonly string _valueName;
    private readonly TimeProvider _time;
    private UserRegistryValue? _applied;

    /// <param name="definition">Запись каталога с Id из <see cref="RegistryTweakIds"/>; другой Id — ArgumentException.</param>
    /// <param name="valueName">Имя значения: у game-mode — из каталога, у пар — путь к exe игры.</param>
    public RegistryTweakAction(IUserRegistry registry, RegistryTweakDefinition definition, string valueName, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        var displayName = TweakText.Name(definition.Id)
            ?? throw new ArgumentException($"Registry tweak '{definition.Id}' is not in the allow list", nameof(definition));

        _registry = registry;
        _definition = definition;
        _valueName = valueName;
        _time = time;
        Descriptor = new ActionDescriptor(ActionIds.RegistryTweak, displayName, SafetyLevel.L2Confirmed, RequiresAdmin: false,
            RequiresReboot: false, RequiresConfirmation: true, IsReversible: true, AllowedDuringGaming: false, VerifiedForAutomatic: false);
    }

    public string Id => ActionIds.RegistryTweak;

    public ActionDescriptor Descriptor { get; }

    private bool IsPair => _definition.ValueName is null;

    public Task<ActionSupport> CheckSupportAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (current, applied, reason) = Evaluate();
        if (reason is not null)
            return Task.FromResult(new ActionSupport(false, reason, Descriptor.DisplayName));

        var summary = string.Format(CultureInfo.CurrentCulture, ReportStrings.RegistryTweakSummaryFormat, Descriptor.DisplayName,
            current?.Data ?? ReportStrings.RegistryTweakValueAbsent, applied!.Data);
        return Task.FromResult(new ActionSupport(true, null, summary));
    }

    public Task<ActionSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (original, applied, reason) = Evaluate();
        if (reason is not null)
            throw new InvalidOperationException(reason);

        _applied = applied!;
        return Task.FromResult(UserRegistrySnapshot.Capture(Id, _definition.SubKey, _valueName, original, applied!, _time.GetUtcNow()));
    }

    public Task<ApplyResult> ApplyAsync(CancellationToken cancellationToken)
    {
        var applied = _applied ?? throw new InvalidOperationException("CaptureAsync must precede ApplyAsync.");
        cancellationToken.ThrowIfCancellationRequested();
        _registry.Write(_definition.SubKey, _valueName, applied);
        return Task.FromResult(ApplyResult.Done);
    }

    private (UserRegistryValue? Current, UserRegistryValue? Applied, string? Reason) Evaluate()
    {
        UserRegistryValue? current;
        try
        {
            current = _registry.Read(_definition.SubKey, _valueName);
        }
        catch (InvalidDataException)
        {
            return (null, null, ReportStrings.Skip_RegistryTweakUnexpectedType);
        }

        if (IsPair)
        {
            if (current is not null && current.Kind != UserRegistryValueKind.String)
                return (current, null, ReportStrings.Skip_RegistryTweakUnexpectedType);
            return GpuPreferenceString.Contains(current?.Data, _definition.Value)
                ? (current, null, ReportStrings.Skip_RegistryTweakAlreadySet)
                : (current, UserRegistryValue.FromString(GpuPreferenceString.Set(current?.Data, _definition.Value)), null);
        }

        var applied = UserRegistryValue.FromDWord(uint.Parse(_definition.Value, NumberStyles.None, CultureInfo.InvariantCulture));
        if (current is not null && current.Kind != UserRegistryValueKind.DWord)
            return (current, null, ReportStrings.Skip_RegistryTweakUnexpectedType);
        return current == applied
            ? (current, null, ReportStrings.Skip_RegistryTweakAlreadyEnabled)
            : (current, applied, null);
    }
}
