using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Recovery;
using Booster.Core.Reports;
using Booster.Core.Resources;
using Booster.Core.Sensors;

namespace Booster.Core.Profiles;

public sealed record GameProcess(string Name, ProcessIdentity Identity);

public sealed record PlannedAction(IReversibleAction Action, ActionMode? Mode, PolicyContext Context, ActionSupport Support,
    PolicyDecision Decision)
{
    public bool WillApply => Support.IsSupported && Decision.Verdict == PolicyVerdict.Allow;

    public string SkipReason => Support.IsSupported ? EnumText.Format(Decision.Reason) : Support.Reason ?? ReportStrings.Skip_NotSupported;
}

public sealed record SessionPlan(GamingProfile Profile, GameProcess? Game, ExecutionTrigger Trigger, IReadOnlyList<PlannedAction> Actions);

public sealed record SessionProgress(SessionState State, SessionRecord? Session, SystemLoad? Load, bool GameRunning,
    SensorSnapshot? Sensors = null, ThrottleAssessment? Throttle = null, SessionFrameSummary? Frames = null);
