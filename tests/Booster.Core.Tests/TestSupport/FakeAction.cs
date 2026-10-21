using Booster.Core.Abstractions;
using static Booster.Core.Tests.TestSupport.SessionFactory;

namespace Booster.Core.Tests.TestSupport;

public sealed class FakeAction(FakeSystemState state, string id, string applied = "boost") : IReversibleAction
{
    public string Id { get; } = id;

    public ActionDescriptor Descriptor { get; init; } = new(id, $"Действие {id}", SafetyLevel.L1Reversible,
        RequiresAdmin: false, RequiresReboot: false, RequiresConfirmation: false, IsReversible: true,
        AllowedDuringGaming: true, VerifiedForAutomatic: true);

    public ActionSupport Support { get; set; } = new(true, null, $"{id}: → {applied}");

    public ApplyResult ApplyResult { get; set; } = ApplyResult.Done;

    public bool ThrowOnCheck { get; set; }

    public bool ThrowOnCapture { get; set; }

    public bool ThrowOnApply { get; set; }

    public string? SnapshotActionId { get; set; }

    public Action? OnApply { get; set; }

    public int Checks { get; private set; }

    public int Captures { get; private set; }

    public int Applies { get; private set; }

    public Task<ActionSupport> CheckSupportAsync(CancellationToken cancellationToken)
    {
        Checks++;
        if (ThrowOnCheck)
            throw new InvalidOperationException("check failed");
        return Task.FromResult(Support);
    }

    public Task<ActionSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        Captures++;
        if (ThrowOnCapture)
            throw new InvalidOperationException("capture failed");
        var original = state[Id] ?? "absent";
        return Task.FromResult(new ActionSnapshot(1, SnapshotActionId ?? Id, Json(original), Json(applied), Start));
    }

    public Task<ApplyResult> ApplyAsync(CancellationToken cancellationToken)
    {
        Applies++;
        state.Record($"apply:{Id}");
        OnApply?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        if (ThrowOnApply)
            throw new InvalidOperationException("apply failed");
        if (ApplyResult.Applied)
            state[Id] = applied;
        return Task.FromResult(ApplyResult);
    }
}
