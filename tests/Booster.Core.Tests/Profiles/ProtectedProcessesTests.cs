using Booster.Core.Profiles;

namespace Booster.Core.Tests.Profiles;

public class ProtectedProcessesTests
{
    [Theory]
    [InlineData("EAAntiCheat.GameService")]
    [InlineData("BEService_x64")]
    [InlineData("PnkBstrA")]
    [InlineData("PnkBstrB")]
    [InlineData("FACEITService")]
    [InlineData("faceitservice.exe")]
    public void New_anticheat_services_are_protected(string name)
    {
        Assert.True(ProtectedProcesses.IsProtected(name));
    }
}
