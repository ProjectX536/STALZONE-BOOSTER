using Booster.Core.Recovery;
using Booster.Core.Profiles;
using static Booster.Core.Tests.TestSupport.SessionFactory;

namespace Booster.Core.Tests.Profiles;

public sealed class ActiveSessionActionsTests
{
    [Fact]
    public void Only_applied_changes_are_active()
    {
        var session = Session(changes:
        [
            Change("b"),
            Change("a", status: ChangeStatus.Restored),
            Change("c", status: ChangeStatus.Applied),
            Change("b"),
        ]);

        Assert.Equal(["b", "c"], ActiveSessionActions.AppliedIn(session));
    }

    [Fact]
    public void No_session_is_empty()
    {
        Assert.Empty(ActiveSessionActions.AppliedIn(null));
    }
}
