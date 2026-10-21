using Booster.Core.Inventory;

namespace Booster.Core.Abstractions;

public interface IHardwareProvider
{
    Task<HardwareInventory> ScanAsync(CancellationToken cancellationToken);
}
