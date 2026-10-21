using System.Diagnostics;
using Booster.Platform.Windows.Monitoring;

namespace Booster.Platform.Windows.Tests.Monitoring;

/// <summary>
/// Живая система, только чтение: счётчики производительности, список процессов и запуск <c>cmd.exe</c>,
/// который ничего не делает, кроме выхода. PresentMon здесь не запускается.
/// </summary>
[Trait("Category", "Host")]
public class MonitoringHostTests
{
    private static readonly string Cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public async Task Process_runner_returns_exit_code()
    {
        var code = await new ProcessRunner().RunAsync(new ProcessLaunch(Cmd, ["/c", "exit 7"], Elevate: false),
            TestContext.Current.CancellationToken);

        Assert.Equal(7, code);
    }

    [Fact(Timeout = 30_000)]
    public async Task Process_runner_stops_the_process_on_cancel()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));
        var watch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ProcessRunner().RunAsync(
            new ProcessLaunch(Cmd, ["/c", "ping -n 30 127.0.0.1 >nul"], Elevate: false), cts.Token));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"{watch.Elapsed}");
    }

    [Fact]
    public void Process_lookup_finds_this_process_by_exe_name()
    {
        using var self = Process.GetCurrentProcess();

        Assert.Contains(Environment.ProcessId, new ProcessLookup().FindIdsByName(self.ProcessName + ".exe"));
    }

    [Fact]
    public void Self_usage_monitor_reports_working_set_and_cpu_share_in_range()
    {
        using var monitor = new SelfUsageMonitor(TimeProvider.System);
        var spin = Stopwatch.StartNew();
        while (spin.ElapsedMilliseconds < 50)
            Thread.SpinWait(1000);

        var sample = monitor.Sample();

        Assert.True(sample.WorkingSetBytes > 0, $"{sample}");
        Assert.InRange(sample.CpuPercent, 0, 100);
    }

    [Fact]
    public void Process_list_has_titled_windows_only_and_not_this_process()
    {
        // На машине без рабочего стола (CI) список может быть пустым; проверяются только свойства записей.
        var entries = new ProcessListSource().GetWindowedProcesses();

        Assert.DoesNotContain(entries, e => e.ProcessId == Environment.ProcessId);
        Assert.All(entries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Name));
            Assert.False(string.IsNullOrWhiteSpace(e.WindowTitle));
        });
    }

    [Fact]
    public void Gpu_engine_counters_of_desktop_window_manager_are_read()
    {
        var dwm = Process.GetProcessesByName("dwm");
        var pid = dwm[0].Id;
        foreach (var process in dwm)
            process.Dispose();
        using var source = new GpuEngineCounterSource();

        source.Sample(pid);
        var samples = source.Sample(pid);

        // Значение — разность двух чтений и на коротком отрезке изредка выходит за 100, поэтому проверяется только знак.
        Assert.NotEmpty(samples);
        Assert.All(samples, s => Assert.True(s.UtilizationPercent >= 0, $"{s}"));
    }
}
