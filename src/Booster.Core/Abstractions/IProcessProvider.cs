namespace Booster.Core.Abstractions;

public sealed record ProcessInfo(int ProcessId, string Name);

public readonly record struct ProcessIdentity(int ProcessId, DateTimeOffset StartTime);

public interface IProcessProvider
{
    IReadOnlyList<ProcessInfo> List();

    ProcessIdentity? Identify(int processId);
}
