namespace Booster.Core.Abstractions;

public enum StartupLocation
{
    UserRun,
    UserRunOnce,
    MachineRun,

    MachineRun32,
    MachineRunOnce,
    UserFolder,
    CommonFolder,
}

public enum StartupState
{
    Enabled,
    Disabled,
    Unknown,
}

public sealed record StartupEntry(string Name, StartupLocation Location, string Command, string? ExecutablePath, StartupState State);

public interface IStartupSource
{
    IReadOnlyList<StartupEntry> Read();
}
