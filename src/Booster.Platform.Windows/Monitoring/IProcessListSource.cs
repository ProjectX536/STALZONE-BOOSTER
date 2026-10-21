namespace Booster.Platform.Windows.Monitoring;

public sealed record ProcessEntry(int ProcessId, string Name, string WindowTitle);

public interface IProcessListSource
{
    IReadOnlyList<ProcessEntry> GetWindowedProcesses();
}
