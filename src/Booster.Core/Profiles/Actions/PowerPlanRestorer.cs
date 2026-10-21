using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Recovery;
using Booster.Core.Resources;

namespace Booster.Core.Profiles.Actions;

public sealed class PowerPlanRestorer(IPowerPlanProvider power) : IActionRestorer
{
    public string ActionId => ActionIds.PowerPlan;

    public int SnapshotSchemaVersion => PowerPlanValue.SchemaVersion;

    public Task<ObservedState> InspectAsync(ActionSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ct.ThrowIfCancellationRequested();

        var original = PowerPlanValue.Read(snapshot.OriginalValue).Scheme;
        Guid? applied = snapshot.AppliedValue is { } value ? PowerPlanValue.Read(value).Scheme : null;
        var active = power.GetActiveScheme();
        return Task.FromResult(active == original ? ObservedState.Original
            : active == applied ? ObservedState.Applied
            : ObservedState.Other);
    }

    public Task RevertAsync(ActionSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ct.ThrowIfCancellationRequested();

        var original = PowerPlanValue.Read(snapshot.OriginalValue);
        if (!power.ListSchemes().Any(s => s.Id == original.Scheme))
            throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, ReportStrings.Restore_OriginalSchemeDeletedFormat, original.Name));

        power.SetActiveScheme(original.Scheme);
        return Task.CompletedTask;
    }
}
