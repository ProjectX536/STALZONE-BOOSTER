using Booster.Core.Inventory;

namespace Booster.Core.Abstractions;

public interface IPrivilegeInfo
{
    PrivilegeState GetCurrent();
}
