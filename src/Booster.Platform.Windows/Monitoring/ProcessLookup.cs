using System.Diagnostics;

namespace Booster.Platform.Windows.Monitoring;

/// <summary>Запущенные процессы по имени exe; подменяется в тестах.</summary>
public interface IProcessLookup
{
    /// <summary>PID процессов с этим именем; окончание ".exe" необязательно. Нет таких — пустой список.</summary>
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
