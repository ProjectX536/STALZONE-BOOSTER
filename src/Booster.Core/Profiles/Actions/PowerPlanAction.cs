using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Booster.Core.Abstractions;
using Booster.Core.Resources;

namespace Booster.Core.Profiles.Actions;

public sealed class PowerPlanAction(IPowerPlanProvider power, IReadOnlyList<Guid> targetSchemes, TimeProvider time) : IReversibleAction
{
    private Guid? _captured;

    public string Id => ActionIds.PowerPlan;

    public ActionDescriptor Descriptor { get; } = new(ActionIds.PowerPlan, ReportStrings.Action_PowerPlan, SafetyLevel.L1Reversible,
        RequiresAdmin: false, RequiresReboot: false, RequiresConfirmation: false, IsReversible: true,
        AllowedDuringGaming: true, VerifiedForAutomatic: false);

    public Task<ActionSupport> CheckSupportAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (active, target, reason) = Evaluate();
        return Task.FromResult(reason is null
            ? new ActionSupport(true, null, string.Format(CultureInfo.CurrentCulture, ReportStrings.PowerPlanSummaryFormat, active!.Name, target!.Name))
            : new ActionSupport(false, reason, ReportStrings.Action_PowerPlan));
    }

    public Task<ActionSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (active, target, reason) = Evaluate();
        if (reason is not null)
            throw new InvalidOperationException(reason);

        _captured = target!.Id;
        return Task.FromResult(new ActionSnapshot(PowerPlanValue.SchemaVersion, Id, PowerPlanValue.Write(active!), PowerPlanValue.Write(target),
            time.GetUtcNow()));
    }

    public Task<ApplyResult> ApplyAsync(CancellationToken cancellationToken)
    {
        var target = _captured ?? throw new InvalidOperationException("CaptureAsync must precede ApplyAsync.");
        cancellationToken.ThrowIfCancellationRequested();
        power.SetActiveScheme(target);
        return Task.FromResult(ApplyResult.Done);
    }

    private (PowerScheme? Active, PowerScheme? Target, string? Reason) Evaluate()
    {
        switch (power.GetPowerSource())
        {
            case PowerSource.Battery:
                return (null, null, ReportStrings.Skip_OnBattery);
            case PowerSource.Unknown:
                return (null, null, ReportStrings.Skip_PowerSourceUnknown);
        }

        var schemes = power.ListSchemes();
        var target = targetSchemes.Select(id => schemes.FirstOrDefault(s => s.Id == id)).FirstOrDefault(s => s is not null);
        if (target is null)
            return (null, null, ReportStrings.Skip_NoSuitableScheme);

        var activeId = power.GetActiveScheme();
        if (activeId == target.Id)
            return (null, target, ReportStrings.Skip_SchemeAlreadyActive);

        var active = schemes.FirstOrDefault(s => s.Id == activeId);
        return active is null ? (null, target, ReportStrings.Skip_ActiveSchemeUnknown) : (active, target, null);
    }
}

internal sealed record PowerPlanValue(
    [property: JsonPropertyName("scheme")] Guid Scheme,
    [property: JsonPropertyName("name")] string Name)
{
    public const int SchemaVersion = 1;

    public static JsonElement Write(PowerScheme scheme) => JsonSerializer.SerializeToElement(new PowerPlanValue(scheme.Id, scheme.Name));

    public static PowerPlanValue Read(JsonElement value) =>
        value.Deserialize<PowerPlanValue>() ?? throw new JsonException("power-plan snapshot value is null");
}
