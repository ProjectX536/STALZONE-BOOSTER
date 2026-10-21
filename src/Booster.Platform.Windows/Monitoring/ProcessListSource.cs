using System.ComponentModel;
using System.Diagnostics;

namespace Booster.Platform.Windows.Monitoring;

public sealed class ProcessListSource : IProcessListSource
{
    public IReadOnlyList<ProcessEntry> GetWindowedProcesses()
    {
        var ownId = Environment.ProcessId;
        var processes = Process.GetProcesses();
        try
        {
            var entries = new List<ProcessEntry>();
            foreach (var process in processes)
            {
                if (process.Id == ownId)
                    continue;

                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero)
                        continue;

                    var title = process.MainWindowTitle;
                    if (string.IsNullOrWhiteSpace(title))
                        continue;

                    entries.Add(new ProcessEntry(process.Id, process.ProcessName, title));
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    // Процесс уже завершился.
                }
            }

            return entries
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.ProcessId)
                .ToArray();
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }
}
