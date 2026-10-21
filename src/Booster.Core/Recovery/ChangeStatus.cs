namespace Booster.Core.Recovery;

public enum ChangeStatus
{
    Pending,
    Applied,
    Restored,

    RestoreConflict,
    RestoreFailed,
    NeedsReboot,
    NeedsManualCheck,
}

public enum ObservedState
{
    Original,
    Applied,
    Other,

    Gone,
}

public enum SessionState
{
    Idle,
    Preflight,
    Snapshot,
    Apply,
    Gaming,
    Restore,
    Verify,
    Recovery,
}

public enum SessionOutcome
{
    Completed,

    RolledBack,

    Recovered,
}

public enum SessionKind
{
    Game,

    Preparation,

    Maintenance,
}
