using System.Diagnostics;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Monitoring;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(ProcessLaunch launch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ct.ThrowIfCancellationRequested();

        var info = new ProcessStartInfo(launch.FileName, WindowsCommandLine.Join(launch.Arguments))
        {
            UseShellExecute = launch.Elevate,
            CreateNoWindow = !launch.Elevate,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        if (launch.Elevate)
            info.Verb = "runas";

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"{launch.FileName} did not start a new process.");
        launch.Started?.Invoke();
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!launch.Elevate)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Процесс успел завершиться сам.
            }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return process.ExitCode;
    }
}
