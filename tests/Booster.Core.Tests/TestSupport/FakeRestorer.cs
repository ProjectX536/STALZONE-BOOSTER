using Booster.Core.Abstractions;
using Booster.Core.Recovery;

namespace Booster.Core.Tests.TestSupport;

public sealed class FakeRestorer(FakeSystemState state, string actionId) : IActionRestorer
{
    public string ActionId { get; } = actionId;

    public int SnapshotSchemaVersion { get; init; } = 1;

    public bool ThrowOnRevert { get; set; }

    public bool RevertHasNoEffect { get; set; }

    public Action<CancellationToken>? OnRevert { get; set; }

    public int Inspections { get; private set; }

    public Task<ObservedState> InspectAsync(ActionSnapshot snapshot, CancellationToken ct)
    {
        Inspections++;
        var current = state[ActionId];
        var observed = current is null ? ObservedState.Gone
            : current == snapshot.OriginalValue.GetString() ? ObservedState.Original
            : current == snapshot.AppliedValue?.GetString() ? ObservedState.Applied
            : ObservedState.Other;
        return Task.FromResult(observed);
    }

    public Task RevertAsync(ActionSnapshot snapshot, CancellationToken ct)
    {
        state.Record($"revert:{ActionId}");
        OnRevert?.Invoke(ct);
        ct.ThrowIfCancellationRequested();
        if (ThrowOnRevert)
            throw new InvalidOperationException("revert failed");
        if (!RevertHasNoEffect)
            state[ActionId] = snapshot.OriginalValue.GetString();
        return Task.CompletedTask;
    }
}
