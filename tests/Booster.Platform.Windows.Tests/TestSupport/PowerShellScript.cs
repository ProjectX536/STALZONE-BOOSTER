using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Booster.Platform.Windows.Tests.TestSupport;

public sealed record ScriptRun(int ExitCode, string StdOut, string StdErr);

/// <summary>Запуск скриптов из <c>scripts/</c> репозитория в Windows PowerShell 5.1, как их запускает пользователь.</summary>
public static class PowerShellScript
{
    // 04 запускает все сборщики подряд, а Win32_PnPSignedDriver на части систем перечисляется десятки секунд.
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    public static string ScriptsDirectory { get; } = Path.Combine(FindRepositoryRoot(), "scripts");

    public static async Task<ScriptRun> RunAsync(string script, params string[] arguments)
    {
        // stdout скрипты пишут только в ASCII, а ошибки PowerShell — в кодировке консоли, то есть в OEM-кодовой странице (866 в русской Windows).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
        };
        foreach (var argument in (string[])["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(ScriptsDirectory, script)])
            info.ArgumentList.Add(argument);
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("powershell.exe did not start");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Timeout);
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        return new(process.ExitCode, await stdout, await stderr);
    }

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Booster.sln")))
                return dir.FullName;
        }
        throw new InvalidOperationException($"Booster.sln not found above {AppContext.BaseDirectory}");
    }
}
