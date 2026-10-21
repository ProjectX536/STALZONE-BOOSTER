using Booster.Core.Recovery;

namespace Booster.Core.Abstractions;

public interface IActionRestorer
{
    string ActionId { get; }

    int SnapshotSchemaVersion { get; }

    Task<ObservedState> InspectAsync(ActionSnapshot snapshot, CancellationToken ct);

    Task RevertAsync(ActionSnapshot snapshot, CancellationToken ct);
}
