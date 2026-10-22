using Booster.Core.GameNetwork;

namespace Booster.Core.Abstractions;

public interface IGameNetworkStore
{
    Task SaveAsync(GameNetworkMeasurement measurement, CancellationToken ct);

    Task<IReadOnlyList<GameNetworkMeasurement>> LoadAllAsync(CancellationToken ct);

    Task DeleteAsync(string id, CancellationToken ct);
}
