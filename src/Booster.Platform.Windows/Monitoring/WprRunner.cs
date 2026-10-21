using System.ComponentModel;
using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Platform.Windows.Common;
using Microsoft.Extensions.Logging;

namespace Booster.Platform.Windows.Monitoring;

public sealed partial class WprRunner : IWprRunner
{
    private const int ErrorCancelled = 1223;

    private const string TraceExtension = ".etl";

    private static readonly string[] StartArguments = ["-start", "CPU"];
    private static readonly string[] CancelArguments = ["-cancel"];

    private readonly IProcessRunner _runner;
    private readonly IAppPaths _paths;
    private readonly ILogger<WprRunner> _logger;
    private readonly string _executable;

    public WprRunner(IProcessRunner runner, IAppPaths paths, ILogger<WprRunner> logger)
        : this(runner, paths, logger, Environment.SystemDirectory)
    {
    }

    internal WprRunner(IProcessRunner runner, IAppPaths paths, ILogger<WprRunner> logger, string systemDirectory)
    {
        _runner = runner;
        _paths = paths;
        _logger = logger;
        _executable = Path.Combine(systemDirectory, "wpr.exe");
    }

    public bool IsAvailable => File.Exists(_executable);

    public Task<WprResult> StartAsync(CancellationToken ct) => RunAsync(StartArguments, null, ct);

    public Task<WprResult> StopAsync(string fileName, CancellationToken ct)
    {
        var name = FileNames.CheckBare(fileName);
        if (!string.Equals(Path.GetExtension(name), TraceExtension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{fileName}' is not an {TraceExtension} file name", nameof(fileName));
        var path = Path.Combine(_paths.Traces, name);
        return RunAsync(["-stop", path], path, ct);
    }

    public Task<WprResult> CancelAsync(CancellationToken ct) => RunAsync(CancelArguments, null, ct);

    private async Task<WprResult> RunAsync(IReadOnlyList<string> arguments, string? tracePath, CancellationToken ct)
    {
        if (!IsAvailable)
            return new WprResult(WprOutcome.Failed, Detail: $"{_executable} not found");

        int exitCode;
        try
        {
            exitCode = await _runner.RunAsync(new ProcessLaunch(_executable, arguments, Elevate: true), ct).ConfigureAwait(false);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new WprResult(WprOutcome.Declined);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            LogLaunchFailed(arguments[0], ex.GetType().Name, ex.Message);
            return new WprResult(WprOutcome.Failed, Detail: $"wpr.exe did not start: {ex.Message}");
        }

        if (exitCode == 0)
            return new WprResult(WprOutcome.Done, Path: tracePath);
        var code = string.Create(CultureInfo.InvariantCulture, $"0x{exitCode:X8}");
        LogExitCode(arguments[0], code);
        return new WprResult(WprOutcome.Failed, exitCode, $"wpr.exe exit code {code}");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "wpr.exe {Command} did not start: {ExceptionType}: {Message}")]
    private partial void LogLaunchFailed(string command, string exceptionType, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "wpr.exe {Command} exited with {ExitCode}")]
    private partial void LogExitCode(string command, string exitCode);
}
