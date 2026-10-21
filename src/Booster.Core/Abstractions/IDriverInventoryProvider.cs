using Booster.Core.Inventory;

namespace Booster.Core.Abstractions;

public interface IDriverInventoryProvider
{
    Task<DriverInventoryResult> ScanAsync(CancellationToken cancellationToken);
}
