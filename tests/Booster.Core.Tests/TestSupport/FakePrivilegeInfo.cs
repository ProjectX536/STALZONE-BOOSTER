using Booster.Core.Abstractions;
using Booster.Core.Inventory;

namespace Booster.Core.Tests.TestSupport;

/// <summary>РџСЂР°РІР° РїСЂРѕС†РµСЃСЃР° Booster; РїРѕ СѓРјРѕР»С‡Р°РЅРёСЋ вЂ” Р±РµР· РїРѕРІС‹С€РµРЅРёСЏ.</summary>
public sealed class FakePrivilegeInfo : IPrivilegeInfo
{
    public PrivilegeState State { get; set; } = new(IsElevated: false, IsPerformanceLogUser: false);

    public PrivilegeState GetCurrent() => State;
}
