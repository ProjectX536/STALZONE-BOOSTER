using System.Diagnostics;

namespace Booster.Platform.Windows.Monitoring;

public interface IProcessLookup
{
    IReadOnlyList<int> FindIdsByName(string processName);
}

public sealed class ProcessLookup : IProcessLookup
{
    public IReadOnlyList<int> FindIdsByName(string processName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);
        var name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName[..^4] : processName;

        var processes = Process.GetProcessesByName(name);
        try
        {
            return processes.Select(p => p.Id).Order().ToArray();
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }
}
