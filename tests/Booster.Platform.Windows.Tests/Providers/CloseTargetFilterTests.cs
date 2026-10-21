using Booster.Platform.Windows.Providers;

namespace Booster.Platform.Windows.Tests.Providers;

public class CloseTargetFilterTests
{
    private const int OwnPid = 500;

    [Fact]
    public void Protected_name_is_dropped()
    {
        Assert.Equal([2], CloseTargetFilter.Filter([("explorer", 1), ("discord", 2), ("EasyAntiCheat", 3)], OwnPid));
    }

    [Fact]
    public void Own_process_is_dropped()
    {
        Assert.Equal([2], CloseTargetFilter.Filter([("discord", OwnPid), ("discord", 2)], OwnPid));
    }

    [Fact]
    public void Name_with_exe_and_other_case_is_normalized()
    {
        Assert.Equal([2], CloseTargetFilter.Filter([("EXPLORER.EXE", 1), ("Discord.EXE", 2)], OwnPid));
    }

    [Fact]
    public void Empty_name_is_dropped()
    {
        Assert.Equal([2], CloseTargetFilter.Filter([("", 1), ("discord", 2), ("   ", 3)], OwnPid));
    }
}
