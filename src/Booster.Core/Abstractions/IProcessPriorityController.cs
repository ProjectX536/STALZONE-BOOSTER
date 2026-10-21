namespace Booster.Core.Abstractions;

public enum ProcessPriority
{
    Idle,
    BelowNormal,
    Normal,
    AboveNormal,
    High,
    RealTime,
}

public interface IProcessPriorityController
{
    ProcessPriority? GetPriority(ProcessIdentity process);

    bool CanSetPriority(ProcessIdentity process);

    void SetPriority(ProcessIdentity process, ProcessPriority priority);
}
