using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Profiles;
using Booster.Core.Reports;
using Booster.Core.Resources;
using Booster.Core.Tests.TestSupport;

namespace Booster.Core.Tests.Profiles;

public class SessionPlanTests
{
    private static readonly PolicyContext Context = new(ExecutionTrigger.UserInitiated, false, false, false, false);

    private static PlannedAction Planned(ActionSupport support, PolicyDecision decision) =>
        new(new FakeAction(new FakeSystemState(), "a"), ActionMode.AskFirst, Context, support, decision);

    [Fact]
    public void Skip_reason_of_a_supported_action_is_the_policy_reason()
    {
        var planned = Planned(new(true, null, "a"), new(PolicyVerdict.RequireConfirmation, PolicyReason.ConfirmationRequired));

        Assert.Equal(EnumText.Format(PolicyReason.ConfirmationRequired), planned.SkipReason);
    }

    [Fact]
    public void Skip_reason_of_an_unsupported_action_is_its_support_reason()
    {
        var planned = Planned(new(false, "игра не запущена", "a"), new(PolicyVerdict.Allow, PolicyReason.Allowed));

        Assert.Equal("игра не запущена", planned.SkipReason);
    }

    [Fact]
    public void Skip_reason_without_a_support_reason_says_not_supported()
    {
        var planned = Planned(new(false, null, "a"), new(PolicyVerdict.Allow, PolicyReason.Allowed));

        Assert.Equal(ReportStrings.Skip_NotSupported, planned.SkipReason);
    }
}
