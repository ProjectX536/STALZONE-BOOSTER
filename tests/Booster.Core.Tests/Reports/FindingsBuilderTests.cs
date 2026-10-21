using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.Inventory;
using Booster.Core.Metrics;
using Booster.Core.Reports;
using Booster.Core.Resources;
using Booster.Core.Sensors;
using Booster.Core.SystemHealth;
using Booster.Core.Tests.TestSupport;
using static Booster.Core.Tests.TestSupport.SnapshotFactory;

namespace Booster.Core.Tests.Reports;

public class FindingsBuilderTests
{
    private static readonly Thresholds Thresholds = FakeConfiguration.DefaultThresholds;

    private static IReadOnlyList<Finding> Build(SystemSnapshot snapshot, params ConfigWarning[] warnings) =>
        FindingsBuilder.Build(snapshot, Thresholds, warnings);

    [Fact]
    public void Healthy_system_has_no_findings()
    {
        Assert.Empty(Build(Healthy()));
    }

    [Fact]
    public void Device_without_driver_gives_one_device_error()
    {
        var device = Device("Неизвестное устройство") with
        {
            IsSigned = Reading<bool>.Unknown(Source),
            State = Known(DeviceState.Error),
            ProblemCode = Known(28),
        };

        var finding = Assert.Single(Build(Healthy().WithDevices(device)));

        Assert.Equal(FindingKind.DeviceError, finding.Kind);
        Assert.Equal(FindingSeverity.Warning, finding.Severity);
        Assert.Equal("Неизвестное устройство", finding.Subject);
        Assert.Contains("28", finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Disabled_device_is_info()
    {
        var device = Device("Bluetooth") with { State = Known(DeviceState.Disabled), ProblemCode = Known(22) };

        var finding = Assert.Single(Build(Healthy().WithDevices(device)));

        Assert.Equal(FindingKind.DeviceDisabled, finding.Kind);
        Assert.Equal(FindingSeverity.Info, finding.Severity);
    }

    [Fact]
    public void Device_without_name_is_named_by_instance_id()
    {
        var device = Device("unused") with { DeviceName = Reading<string>.Unknown(Source), State = Known(DeviceState.Error) };

        Assert.Equal(device.DeviceInstanceId, Assert.Single(Build(Healthy().WithDevices(device))).Subject);
    }

    [Fact]
    public void Unknown_signature_is_not_reported_as_unsigned()
    {
        var device = Device("Устройство") with { IsSigned = Reading<bool>.Unknown(Source) };

        Assert.Empty(Build(Healthy().WithDevices(device)));
    }

    [Fact]
    public void Unsigned_driver_is_a_warning()
    {
        var device = Device("Устройство") with { IsSigned = Known(false) };

        var finding = Assert.Single(Build(Healthy().WithDevices(device)));

        Assert.Equal(FindingKind.UnsignedDriver, finding.Kind);
        Assert.Equal(FindingSeverity.Warning, finding.Severity);
        Assert.Equal("Устройство", finding.Subject);
    }

    [Fact]
    public void Working_signed_device_has_no_device_findings()
    {
        Assert.Empty(FindingsBuilder.ForDevice(Device("Устройство")));
    }

    [Fact]
    public void Device_findings_match_the_report_findings_for_that_device()
    {
        var device = Device("Устройство") with { State = Known(DeviceState.Error), ProblemCode = Known(43), IsSigned = Known(false) };

        var forDevice = FindingsBuilder.ForDevice(device);

        Assert.Equal([FindingKind.DeviceError, FindingKind.UnsignedDriver], forDevice.Select(f => f.Kind));
        Assert.Equal(forDevice.ToHashSet(), Build(Healthy().WithDevices(device)).ToHashSet());
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(9, true)]
    [InlineData(10, false)]
    [InlineData(50, false)]
    public void Free_space_below_threshold_is_low(long freeGiB, bool expected)
    {
        var findings = Build(Healthy().WithVolumes(Volume("D:", 100 * GiB, freeGiB * GiB)));

        if (expected)
        {
            var finding = Assert.Single(findings);
            Assert.Equal(FindingKind.LowDiskSpace, finding.Kind);
            Assert.Equal(FindingSeverity.Warning, finding.Severity);
            Assert.Equal("D:", finding.Subject);
        }
        else
        {
            Assert.Empty(findings);
        }
    }

    [Fact]
    public void Volume_with_unknown_size_or_free_space_is_skipped()
    {
        var volumes = new[]
        {
            Volume("D:", 100 * GiB, GiB) with { FreeBytes = Reading<long>.Unknown(Source) },
            Volume("E:", 100 * GiB, GiB) with { SizeBytes = Reading<long>.Unknown(Source) },
            Volume("F:", 0, 0),
        };

        Assert.Empty(Build(Healthy().WithVolumes(volumes)));
    }

    [Fact]
    public void Failed_section_is_a_warning()
    {
        var snapshot = Healthy();
        snapshot = snapshot with
        {
            Hardware = snapshot.Hardware with { Gpu = Reading<GpuInventory>.Failure("DXGI", "timeout after 15000 ms") },
        };

        var finding = Assert.Single(Build(snapshot));

        Assert.Equal(FindingKind.SectionFailed, finding.Kind);
        Assert.Equal(FindingSeverity.Warning, finding.Severity);
        Assert.Contains("timeout after 15000 ms", finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Failed_driver_inventory_is_a_section_failure()
    {
        var snapshot = Healthy() with { Drivers = Reading<DriverInventoryResult>.Failure("WMI", "RPC server unavailable") };

        Assert.Equal(FindingKind.SectionFailed, Assert.Single(Build(snapshot)).Kind);
    }

    [Fact]
    public void Section_requiring_admin_is_info()
    {
        var snapshot = Healthy();
        snapshot = snapshot with
        {
            Hardware = snapshot.Hardware with { Storage = Reading<IReadOnlyList<StorageDeviceInfo>>.NeedsAdmin("MSFT_PhysicalDisk") },
        };

        var finding = Assert.Single(Build(snapshot));

        Assert.Equal(FindingKind.SectionRequiresAdmin, finding.Kind);
        Assert.Equal(FindingSeverity.Info, finding.Severity);
    }

    [Fact]
    public void Unknown_section_is_not_a_finding()
    {
        var snapshot = Healthy();
        snapshot = snapshot with { Hardware = snapshot.Hardware with { Displays = Reading<IReadOnlyList<DisplayInfo>>.Unknown("DisplayConfig") } };

        Assert.Empty(Build(snapshot));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public void Frame_capture_needs_elevation_without_admin_or_performance_log_users(bool elevated, bool performanceLogUser,
        bool expected)
    {
        var findings = Build(Healthy() with { Privileges = new PrivilegeState(elevated, performanceLogUser) });

        if (expected)
        {
            var finding = Assert.Single(findings);
            Assert.Equal(FindingKind.FrameCaptureNeedsElevation, finding.Kind);
            Assert.Equal(FindingSeverity.Info, finding.Severity);
        }
        else
        {
            Assert.Empty(findings);
        }
    }

    [Fact]
    public void Each_config_warning_becomes_a_finding()
    {
        var findings = Build(Healthy(),
            new ConfigWarning(ConfigWarningKind.InvalidJson, ConfigFiles.Settings, "line 3: unexpected token"),
            new ConfigWarning(ConfigWarningKind.ValueClamped, ConfigFiles.Thresholds, "minimumFramesForMetrics 5 → 60"));

        Assert.Collection(findings,
            f =>
            {
                Assert.Equal((FindingKind.ConfigWarning, FindingSeverity.Warning, ConfigFiles.Settings), (f.Kind, f.Severity, f.Subject));
                Assert.Contains("line 3: unexpected token", f.Detail, StringComparison.Ordinal);
            },
            f => Assert.Equal((FindingKind.ConfigWarning, ConfigFiles.Thresholds), (f.Kind, f.Subject)));
    }

    [Fact]
    public void Warnings_come_before_info()
    {
        var snapshot = Healthy().WithDevices(
            Device("Отключённое") with { State = Known(DeviceState.Disabled) },
            Device("Сломанное") with { State = Known(DeviceState.Error) });

        var findings = Build(snapshot with { Privileges = new PrivilegeState(false, false) });

        Assert.Equal(new[] { FindingSeverity.Warning, FindingSeverity.Info, FindingSeverity.Info }, findings.Select(f => f.Severity));
        Assert.Equal("Сломанное", findings[0].Subject);
    }

    private static BenchmarkReport Benchmark(ThrottleAssessment? throttle)
    {
        var series = SampleSeries.Full();
        return new BenchmarkReport(series with { Telemetry = series.Telemetry with { Throttle = throttle } }, null, [],
            FakeConfiguration.DefaultThresholds.Comparison);
    }

    private static BenchmarkReport Benchmark(params ThrottleFinding[] findings) => Benchmark(new ThrottleAssessment(10, 2, true, findings));

    private static ThrottleFinding Throttle(ThrottleSignal signal, ThrottleConfidence confidence) =>
        new(signal, confidence, GpuName, 3, 10, Reading<double>.Known(87, Source));

    [Fact]
    public void Throttling_finding_from_benchmark_telemetry()
    {
        var benchmark = Benchmark(
            Throttle(ThrottleSignal.GpuDriverThermal, ThrottleConfidence.High),
            Throttle(ThrottleSignal.GpuTemperatureAtLimit, ThrottleConfidence.Medium),
            Throttle(ThrottleSignal.CpuPerformanceLimited, ThrottleConfidence.Low));

        var finding = Assert.Single(FindingsBuilder.Build(Healthy(), Thresholds, [], benchmark));

        Assert.Equal(FindingKind.ThrottlingDetected, finding.Kind);
        Assert.Equal(FindingSeverity.Warning, finding.Severity);
        Assert.Equal(ReportStrings.SectionBenchmark, finding.Subject);
        Assert.Contains(EnumText.Format(ThrottleSignal.GpuDriverThermal), finding.Detail, StringComparison.Ordinal);
        Assert.Contains(EnumText.Format(ThrottleSignal.GpuTemperatureAtLimit), finding.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(EnumText.Format(ThrottleSignal.CpuPerformanceLimited), finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Low_only_findings_give_no_warning()
    {
        var benchmark = Benchmark(
            Throttle(ThrottleSignal.GpuClockDropUnderLoad, ThrottleConfidence.Low),
            Throttle(ThrottleSignal.CpuPerformanceLimited, ThrottleConfidence.Low));

        Assert.Empty(FindingsBuilder.Build(Healthy(), Thresholds, [], benchmark));
    }

    [Fact]
    public void Series_without_sensor_samples_gives_no_throttle_warning()
    {
        Assert.Empty(FindingsBuilder.Build(Healthy(), Thresholds, [], Benchmark((ThrottleAssessment?)null)));
    }

    [Fact]
    public void Warning_advice_becomes_finding()
    {
        var advice = new SystemAdvice(AdviceId.BugChecks, AdviceModule.Stability, FindingSeverity.Warning, SafetyLevel.L3ManualOnly,
            ["2", "0x0000009f"]);

        var finding = Assert.Single(FindingsBuilder.Build(Healthy(), Thresholds, [], advice: [advice]));

        Assert.Equal(new Finding(FindingKind.SystemAdvice, FindingSeverity.Warning, EnumText.Format(AdviceId.BugChecks),
            EnumText.Format(AdviceModule.Stability)), finding);
    }

    [Fact]
    public void Info_advice_is_not_a_finding()
    {
        var advice = new SystemAdvice(AdviceId.PagefileManual, AdviceModule.Memory, FindingSeverity.Info, SafetyLevel.L3ManualOnly,
            [@"C:\pagefile.sys"]);

        Assert.Empty(FindingsBuilder.Build(Healthy(), Thresholds, [], advice: [advice]));
    }
}
