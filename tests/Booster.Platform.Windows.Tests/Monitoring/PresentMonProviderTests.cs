using System.ComponentModel;
using Booster.Core.Abstractions;
using Booster.Core.Engine;
using Booster.Core.Inventory;
using Booster.Core.Tests.TestSupport;
using Booster.Platform.Windows.Monitoring;
using Booster.Platform.Windows.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace Booster.Platform.Windows.Tests.Monitoring;

public sealed class PresentMonProviderTests
{
    private const string Session = "BoosterCapture";
    private const int ErrorCancelled = 1223;
    private static readonly AdapterLuid Dgpu = new(0xC2A1, 0);
    private static readonly FrameCaptureRequest ByName = new("firefox.exe", null, TimeSpan.FromSeconds(60));

    private readonly FakeAppPaths _paths = new();
    private readonly FakeFileProbe _files = new();
    private readonly FakeRunner _runner = new();
    private readonly FakeSampler _sampler = new();
    private readonly FakeProcessLookup _processes = new();
    private readonly CapabilityRegistry _capabilities = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 17, 14, 30, 5, TimeSpan.Zero));
    private PrivilegeState _privileges = new(IsElevated: true, IsPerformanceLogUser: false);

    public PresentMonProviderTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        _files.Add(Exe);
        _processes.Add("firefox.exe", 10348);
        _runner.OnRun = (launch, _) => WriteCsv(launch, "pm26-firefox.csv");
    }

    private string Exe => Path.Combine(_paths.Tools, "PresentMon", "PresentMon-2.6.0-x64.exe");

    private string CsvPath(string label) => Path.Combine(_paths.Captures, $"20261017-143005-{label}.csv");

    private static string OutputFile(ProcessLaunch launch) => launch.Arguments[launch.Arguments.ToList().IndexOf("--output_file") + 1];

    private Task<int> WriteCsv(ProcessLaunch launch, string fixture)
    {
        _files.Add(OutputFile(launch), File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PresentMon", fixture)));
        return Task.FromResult(0);
    }

    private PresentMonProvider CreateProvider()
    {
        var config = new FakeConfiguration();
        return new PresentMonProvider(new PresentMonLocator(_files, config, _paths), _runner, _files, _paths,
            new FakePrivilegeInfo(() => _privileges), _sampler, _processes, config, _capabilities, _time,
            new ListLogger<PresentMonProvider>());
    }

    private Task<FrameCaptureResult> Capture(FrameCaptureRequest? request = null) =>
        CreateProvider().CaptureAsync(request ?? ByName, null, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Missing_presentmon_is_not_installed_and_nothing_starts()
    {
        _files.Remove(Exe);

        var result = await Capture();

        Assert.Equal(FrameCaptureStatus.NotInstalled, result.Status);
        Assert.Empty(_runner.Calls);
        Assert.Empty(_sampler.ProcessIds);
    }

    [Fact]
    public async Task Successful_capture_parses_csv_and_takes_adapter_from_sampler()
    {
        var result = await Capture();

        Assert.Equal(FrameCaptureStatus.Completed, result.Status);
        Assert.Equal(400, result.Capture!.Frames.Count);
        Assert.Equal(10348, result.Capture.ProcessId);
        Assert.Equal(CsvPath("firefox.exe"), result.Capture.CsvPath);
        Assert.Equal(Dgpu, result.RenderAdapter.Value);
        Assert.Equal([10348], _sampler.ProcessIds);
        Assert.True(_sampler.WasStopped);
    }

    [Fact]
    public async Task Presentmon_gets_capture_arguments_and_writes_into_captures()
    {
        await Capture();

        var launch = Assert.Single(_runner.Calls).Launch;
        Assert.Equal(Exe, launch.FileName);
        Assert.Equal(PresentMonArguments.ForCapture(ByName, CsvPath("firefox.exe"), Session), launch.Arguments);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    public async Task Uac_prompt_is_used_only_without_elevation_or_performance_log_users(bool elevated, bool perfLogUser,
        bool expectElevate)
    {
        _privileges = new PrivilegeState(elevated, perfLogUser);

        await Capture();

        Assert.Equal(expectElevate, Assert.Single(_runner.Calls).Launch.Elevate);
    }

    [Fact]
    public async Task Declined_uac_prompt_is_elevation_declined()
    {
        _privileges = new PrivilegeState(false, false);
        _runner.OnRun = (_, _) => throw new Win32Exception(ErrorCancelled);

        var result = await Capture();

        Assert.Equal(FrameCaptureStatus.ElevationDeclined, result.Status);
        Assert.Null(result.Capture);
        Assert.True(_sampler.WasStopped);
    }

    [Fact]
    public async Task Started_callback_is_invoked_when_runner_starts_process()
    {
        var startedCalls = 0;
        _runner.OnRun = (launch, _) =>
        {
            launch.Started?.Invoke();
            return WriteCsv(launch, "pm26-firefox.csv");
        };

        var result = await CreateProvider().CaptureAsync(ByName, () => startedCalls++, TestContext.Current.CancellationToken);

        Assert.Equal(FrameCaptureStatus.Completed, result.Status);
        Assert.Equal(1, startedCalls);
    }

    [Fact]
    public async Task Started_is_not_invoked_when_elevation_is_declined()
    {
        _privileges = new PrivilegeState(false, false);
        _runner.OnRun = (_, _) => throw new Win32Exception(ErrorCancelled);
        var startedCalls = 0;

        var result = await CreateProvider().CaptureAsync(ByName, () => startedCalls++, TestContext.Current.CancellationToken);

        Assert.Equal(FrameCaptureStatus.ElevationDeclined, result.Status);
        Assert.Equal(0, startedCalls);
    }

    [Fact]
    public async Task Start_failure_is_failed_with_reason()
    {
        _runner.OnRun = (_, _) => throw new Win32Exception(2, "The system cannot find the file specified.");

        var result = await Capture();

        Assert.Equal(FrameCaptureStatus.Failed, result.Status);
        Assert.Contains("cannot find the file", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_zero_exit_code_is_failed_with_the_code()
    {
        _runner.OnRun = (_, _) => Task.FromResult(5);

        var result = await Capture();

        Assert.Equal(FrameCaptureStatus.Failed, result.Status);
        Assert.Contains("5", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_csv_after_clean_exit_means_the_target_presented_no_frames()
    {
        _runner.OnRun = (_, _) => Task.FromResult(0);

        var result = await Capture();

        Assert.Equal(FrameCaptureStatus.TargetNotFound, result.Status);
        Assert.NotNull(result.Detail);
    }

    [Fact]
    public async Task Csv_status_is_passed_through()
    {
        _runner.OnRun = (launch, _) => WriteCsv(launch, "pm-header-only.csv");

        var result = await Capture();

        Assert.Equal(FrameCaptureStatus.TargetNotFound, result.Status);
        Assert.Equal(Dgpu, result.RenderAdapter.Value);
    }

    [Fact]
    public async Task Cancel_in_direct_mode_closes_the_etw_session()
    {
        var started = new TaskCompletionSource();
        _runner.OnRun = async (launch, ct) =>
        {
            if (launch.Arguments.Contains("--terminate_existing_session"))
                return 0;
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var capture = CreateProvider().CaptureAsync(ByName, null, cts.Token);
        await started.Task;
        await cts.CancelAsync();
        var result = await capture;

        Assert.Equal(FrameCaptureStatus.Cancelled, result.Status);
        Assert.Equal(2, _runner.Calls.Count);
        var (terminate, token) = _runner.Calls[1];
        Assert.Equal(PresentMonArguments.ForTerminate(Session), terminate.Arguments);
        Assert.False(terminate.Elevate);
        Assert.False(token.CanBeCanceled);
        Assert.True(_sampler.WasStopped);
    }

    [Fact]
    public async Task Game_session_capture_starts_and_closes_its_own_etw_session()
    {
        var request = new FrameCaptureRequest(null, 10348, TimeSpan.FromSeconds(300))
        {
            StopWhenProcessExits = true,
            Purpose = FrameCapturePurpose.GameSession,
        };
        var gameSession = PresentMonArguments.SessionName(Session, FrameCapturePurpose.GameSession);
        var started = new TaskCompletionSource();
        _runner.OnRun = async (launch, ct) =>
        {
            if (launch.Arguments.Contains("--terminate_existing_session"))
                return 0;
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var capture = CreateProvider().CaptureAsync(request, null, cts.Token);
        await started.Task;
        await cts.CancelAsync();
        await capture;

        Assert.Equal(PresentMonArguments.ForCapture(request, CsvPath("pid10348"), gameSession), _runner.Calls[0].Launch.Arguments);
        Assert.Equal(PresentMonArguments.ForTerminate(gameSession), _runner.Calls[1].Launch.Arguments);
    }

    [Fact]
    public async Task Cancel_in_elevated_mode_leaves_presentmon_to_finish_by_itself()
    {
        _privileges = new PrivilegeState(false, false);
        _runner.OnRun = async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var capture = CreateProvider().CaptureAsync(ByName, null, cts.Token);
        await cts.CancelAsync();
        var result = await capture;

        Assert.Equal(FrameCaptureStatus.Cancelled, result.Status);
        Assert.Single(_runner.Calls);
        Assert.Contains("60", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_by_process_id_samples_that_process()
    {
        var result = await Capture(new FrameCaptureRequest(null, 10348, TimeSpan.FromSeconds(60)));

        Assert.Equal(FrameCaptureStatus.Completed, result.Status);
        Assert.Equal([10348], _sampler.ProcessIds);
        Assert.Empty(_processes.Queries);
        Assert.Equal(CsvPath("pid10348"), OutputFile(_runner.Calls[0].Launch));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Adapter_is_unknown_without_exactly_one_running_process(int running)
    {
        _processes.Clear();
        for (var i = 0; i < running; i++)
            _processes.Add("firefox.exe", 10348 + i);

        var result = await Capture();

        Assert.Equal(FrameCaptureStatus.Completed, result.Status);
        Assert.Equal(DataStatus.Unknown, result.RenderAdapter.Status);
        Assert.NotNull(result.RenderAdapter.Note);
        Assert.Empty(_sampler.ProcessIds);
    }

    [Fact]
    public async Task Characters_not_allowed_in_file_names_are_replaced()
    {
        await Capture(new FrameCaptureRequest("My:Game?.exe", null, TimeSpan.FromSeconds(60)));

        Assert.Equal(CsvPath("My_Game_.exe"), OutputFile(_runner.Calls[0].Launch));
    }

    [Fact]
    public async Task Invalid_request_is_rejected_before_anything_starts()
    {
        _files.Remove(Exe);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => Capture(new FrameCaptureRequest("a.exe", 1, TimeSpan.FromSeconds(60))));
        Assert.Empty(_runner.Calls);
    }

    [Theory]
    [InlineData(true, false, FrameCaptureMode.Direct, CapabilityStatus.Supported)]
    [InlineData(false, true, FrameCaptureMode.Direct, CapabilityStatus.Supported)]
    [InlineData(false, false, FrameCaptureMode.ElevationPrompt, CapabilityStatus.RequiresAdmin)]
    public async Task Availability_reports_mode_and_capability(bool elevated, bool perfLogUser, FrameCaptureMode mode,
        CapabilityStatus status)
    {
        _privileges = new PrivilegeState(elevated, perfLogUser);

        var availability = await CreateProvider().CheckAvailabilityAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new FrameCaptureAvailability(true, Exe, "2.6.0", mode), availability);
        Assert.Equal(status, _capabilities.Get(CapabilityIds.FrameCapture).Status);
    }

    [Fact]
    public async Task Availability_without_presentmon_is_not_installed()
    {
        _files.Remove(Exe);

        var availability = await CreateProvider().CheckAvailabilityAsync(TestContext.Current.CancellationToken);

        Assert.False(availability.IsInstalled);
        Assert.Null(availability.ExecutablePath);
        var report = _capabilities.Get(CapabilityIds.FrameCapture);
        Assert.Equal(CapabilityStatus.NotSupported, report.Status);
        Assert.NotNull(report.Note);
    }

    private sealed class FakeRunner : IProcessRunner
    {
        public List<(ProcessLaunch Launch, CancellationToken Token)> Calls { get; } = [];

        public Func<ProcessLaunch, CancellationToken, Task<int>> OnRun { get; set; } = (_, _) => Task.FromResult(0);

        public Task<int> RunAsync(ProcessLaunch launch, CancellationToken ct)
        {
            Calls.Add((launch, ct));
            return OnRun(launch, ct);
        }
    }

    private sealed class FakeSampler : IGpuAdapterUsageSampler
    {
        private readonly List<int> _processIds = [];

        public IReadOnlyList<int> ProcessIds
        {
            get { lock (_processIds) return _processIds.ToArray(); }
        }

        public bool WasStopped { get; private set; }

        public async Task<Reading<AdapterLuid>> SampleUntilCancelledAsync(int processId, CancellationToken ct)
        {
            lock (_processIds)
                _processIds.Add(processId);
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                WasStopped = true;
            }
            return Reading<AdapterLuid>.Known(Dgpu, "fake sampler");
        }
    }

    private sealed class FakeProcessLookup : IProcessLookup
    {
        private readonly List<(string Name, int Id)> _processes = [];

        public List<string> Queries { get; } = [];

        public void Add(string name, int id) => _processes.Add((name, id));

        public void Clear() => _processes.Clear();

        public IReadOnlyList<int> FindIdsByName(string processName)
        {
            Queries.Add(processName);
            return _processes.Where(p => string.Equals(p.Name, processName, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Id).ToArray();
        }
    }

    private sealed class FakePrivilegeInfo(Func<PrivilegeState> state) : IPrivilegeInfo
    {
        public PrivilegeState GetCurrent() => state();
    }
}
