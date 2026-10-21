using System.ComponentModel;
using Booster.Core.Abstractions;
using Booster.Platform.Windows.Common;
using Booster.Platform.Windows.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booster.Platform.Windows.Tests.Monitoring;

/// <summary>Только на фейке <see cref="IProcessRunner"/>: wpr.exe на машине разработчика не запускается.</summary>
public sealed class WprRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Booster.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _system;
    private readonly AppPaths _paths;
    private readonly FakeRunner _runner = new();

    public WprRunnerTests()
    {
        _system = Path.Combine(_root, "System32");
        Directory.CreateDirectory(_system);
        File.WriteAllText(Path.Combine(_system, "wpr.exe"), string.Empty);
        _paths = new AppPaths(Path.Combine(_root, "data"), Path.Combine(_root, "app"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private WprRunner Runner() => new(_runner, _paths, NullLogger<WprRunner>.Instance, _system);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Start_runs_system_wpr_elevated_with_fixed_arguments()
    {
        var result = await Runner().StartAsync(Ct);

        Assert.Equal(new WprResult(WprOutcome.Done), result);
        var launch = Assert.Single(_runner.Launches);
        Assert.Equal(Path.Combine(_system, "wpr.exe"), launch.FileName);
        Assert.Equal(["-start", "CPU"], launch.Arguments);
        Assert.True(launch.Elevate);
    }

    [Fact]
    public async Task Stop_writes_into_traces_folder()
    {
        var result = await Runner().StopAsync("x.etl", Ct);

        var expected = Path.Combine(_root, "data", "traces", "x.etl");
        Assert.Equal(["-stop", expected], Assert.Single(_runner.Launches).Arguments);
        Assert.Equal(new WprResult(WprOutcome.Done, Path: expected), result);
        Assert.True(Directory.Exists(_paths.Traces));
    }

    [Theory]
    [InlineData(@"..\x.etl")]
    [InlineData(@"C:\x.etl")]
    [InlineData("x.txt")]
    [InlineData("")]
    public async Task Stop_rejects_names_outside_traces(string fileName)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Runner().StopAsync(fileName, Ct));

        Assert.Empty(_runner.Launches);
    }

    [Fact]
    public async Task Cancel_uses_cancel_argument()
    {
        await Runner().CancelAsync(Ct);

        var launch = Assert.Single(_runner.Launches);
        Assert.Equal(["-cancel"], launch.Arguments);
        Assert.True(launch.Elevate);
    }

    [Fact]
    public async Task Uac_refusal_is_declined()
    {
        _runner.OnRun = _ => throw new Win32Exception(1223);

        Assert.Equal(new WprResult(WprOutcome.Declined), await Runner().StartAsync(Ct));
    }

    [Fact]
    public async Task Other_launch_error_is_failed_without_code()
    {
        _runner.OnRun = _ => throw new Win32Exception(2, "not found");

        var result = await Runner().CancelAsync(Ct);

        Assert.Equal(WprOutcome.Failed, result.Outcome);
        Assert.Null(result.ExitCode);
        Assert.Contains("not found", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nonzero_exit_is_failed_with_code()
    {
        _runner.OnRun = _ => Task.FromResult(unchecked((int)0xC5583001));

        var result = await Runner().StopAsync("x.etl", Ct);

        Assert.Equal(WprOutcome.Failed, result.Outcome);
        Assert.Equal(unchecked((int)0xC5583001), result.ExitCode);
        Assert.Null(result.Path);
    }

    [Fact]
    public async Task Missing_wpr_is_unavailable_and_not_launched()
    {
        File.Delete(Path.Combine(_system, "wpr.exe"));
        var runner = Runner();

        Assert.False(runner.IsAvailable);
        var result = await runner.StartAsync(Ct);

        Assert.Equal(WprOutcome.Failed, result.Outcome);
        Assert.Null(result.ExitCode);
        Assert.Empty(_runner.Launches);
    }

    private sealed class FakeRunner : IProcessRunner
    {
        public List<ProcessLaunch> Launches { get; } = [];

        public Func<ProcessLaunch, Task<int>> OnRun { get; set; } = _ => Task.FromResult(0);

        public Task<int> RunAsync(ProcessLaunch launch, CancellationToken ct)
        {
            Launches.Add(launch);
            return OnRun(launch);
        }
    }
}
