using Booster.Core.Abstractions;
using Booster.Core.Inventory;

namespace Booster.Platform.Windows.Hardware;

public interface IFirmwareTypeSource
{
    Reading<FirmwareKind> Get();
}
