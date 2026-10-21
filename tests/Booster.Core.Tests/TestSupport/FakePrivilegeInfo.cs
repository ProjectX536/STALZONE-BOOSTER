using Booster.Core.Abstractions;
using Booster.Core.Inventory;

namespace Booster.Core.Tests.TestSupport;

public sealed class FakePrivilegeInfo : IPrivilegeInfo
{
    public PrivilegeState State { get; set; } = new(IsElevated: false, IsPerformanceLogUser: false);

    public PrivilegeState GetCurrent() => State;
}
