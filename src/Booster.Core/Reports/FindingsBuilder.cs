using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.Inventory;
using Booster.Core.Resources;
using Booster.Core.Sensors;
using Booster.Core.SystemHealth;

namespace Booster.Core.Reports;

public static class FindingsBuilder
{
    public const string FrameCaptureSubject = "PresentMon";

    public static IReadOnlyList<Finding> Build(SystemSnapshot snapshot, Thresholds thresholds, IReadOnlyList<ConfigWarning> configWarnings,
        BenchmarkReport? benchmark = null, IReadOnlyList<SystemAdvice>? advice = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentNullException.ThrowIfNull(configWarnings);

        var findings = new List<Finding>();
        foreach (var section in Sections(snapshot))
            AddSection(findings, section);

        if (snapshot.Drivers is { IsAvailable: true, Value: { } drivers })
        {
            foreach (var device in drivers.Devices)
                findings.AddRange(ForDevice(device));
        }

        if (snapshot.Hardware.Storage is { IsAvailable: true, Value: { } disks })
        {
            foreach (var volume in disks.SelectMany(d => d.Volumes))
                AddVolume(findings, volume, thresholds.LowFreeSpacePercent);
        }

        if (!snapshot.Privileges.CanCaptureFramesWithoutPrompt)
        {
            findings.Add(new Finding(FindingKind.FrameCaptureNeedsElevation, FindingSeverity.Info, FrameCaptureSubject,
                "Booster is not elevated and not a member of Performance Log Users"));
        }

        findings.AddRange(configWarnings.Select(w =>
            new Finding(FindingKind.ConfigWarning, FindingSeverity.Warning, w.File, $"{w.Kind}: {w.Detail}")));

        if (benchmark?.Series.Telemetry.Throttle is { } throttle)
            AddThrottling(findings, throttle);

        findings.AddRange((advice ?? []).Where(a => a.Severity == FindingSeverity.Warning).Select(a =>
            new Finding(FindingKind.SystemAdvice, FindingSeverity.Warning, EnumText.Format(a.Id), EnumText.Format(a.Module))));

        return findings.OrderByDescending(f => f.Severity).ToList();
    }

    private readonly record struct SectionState(string Title, DataStatus Status, string? Source, string? Note);

    private static IEnumerable<SectionState> Sections(SystemSnapshot snapshot)
    {
        var hardware = snapshot.Hardware;
        yield return State(ReportStrings.SectionCpu, hardware.Cpu);
        yield return State(ReportStrings.SectionGpu, hardware.Gpu);
        yield return State(ReportStrings.SectionMemory, hardware.Memory);
        yield return State(ReportStrings.SectionBoard, hardware.Board);
        yield return State(ReportStrings.SectionOs, hardware.Os);
        yield return State(ReportStrings.SectionStorage, hardware.Storage);
        yield return State(ReportStrings.SectionDisplays, hardware.Displays);
        yield return State(ReportStrings.SectionDrivers, snapshot.Drivers);
    }

    private static SectionState State<T>(string title, Reading<T> reading) => new(title, reading.Status, reading.Source, reading.Note);

    private static void AddSection(List<Finding> findings, SectionState section)
    {
        var detail = ReadingFormatter.SourceText(section.Source, section.Note);
        if (section.Status == DataStatus.Failed)
            findings.Add(new Finding(FindingKind.SectionFailed, FindingSeverity.Warning, section.Title, detail));
        else if (section.Status == DataStatus.RequiresAdmin)
            findings.Add(new Finding(FindingKind.SectionRequiresAdmin, FindingSeverity.Info, section.Title, detail));
    }

    public static IReadOnlyList<Finding> ForDevice(DeviceDriverInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var findings = new List<Finding>();
        var subject = device.DeviceName is { IsAvailable: true, Value: { Length: > 0 } name } ? name : device.DeviceInstanceId;
        var problem = device.ProblemCode is { IsAvailable: true, Value: var code and not 0 }
            ? string.Create(CultureInfo.InvariantCulture, $"ConfigManagerErrorCode {code}")
            : null;

        if (device.State is { IsAvailable: true, Value: DeviceState.Error })
            findings.Add(new Finding(FindingKind.DeviceError, FindingSeverity.Warning, subject, problem));
        else if (device.State is { IsAvailable: true, Value: DeviceState.Disabled })
            findings.Add(new Finding(FindingKind.DeviceDisabled, FindingSeverity.Info, subject, problem));

        if (device.IsSigned is { IsAvailable: true, Value: false })
            findings.Add(new Finding(FindingKind.UnsignedDriver, FindingSeverity.Warning, subject, DriverText(device)));
        return findings;
    }

    private static string? DriverText(DeviceDriverInfo device)
    {
        var parts = new[] { device.DriverProvider, device.DriverVersion, device.InfName }
            .Where(r => r.IsAvailable && !string.IsNullOrWhiteSpace(r.Value))
            .Select(r => r.Value!)
            .ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static void AddThrottling(List<Finding> findings, ThrottleAssessment assessment)
    {
        var signals = assessment.Findings
            .Where(f => f.Confidence >= ThrottleConfidence.Medium)
            .Select(f => f.Signal)
            .Distinct()
            .Select(s => EnumText.Format(s))
            .ToList();
        if (signals.Count > 0)
        {
            findings.Add(new Finding(FindingKind.ThrottlingDetected, FindingSeverity.Warning, ReportStrings.SectionBenchmark,
                string.Join(ReportStrings.ListSeparator, signals)));
        }
    }

    private static void AddVolume(List<Finding> findings, VolumeInfo volume, double lowFreeSpacePercent)
    {
        if (volume.SizeBytes is not { IsAvailable: true, Value: var size and > 0 }
            || volume.FreeBytes is not { IsAvailable: true, Value: var free and >= 0 })
            return;

        var freePercent = free * 100.0 / size;
        if (freePercent < lowFreeSpacePercent)
        {
            findings.Add(new Finding(FindingKind.LowDiskSpace, FindingSeverity.Warning, volume.DriveLetter,
                string.Create(CultureInfo.InvariantCulture, $"{freePercent:0.0}% free, threshold {lowFreeSpacePercent}%")));
        }
    }
}
