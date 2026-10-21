using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Drivers;
using Booster.Core.Inventory;
using Booster.Core.Resources;
using Booster.Core.Sensors;

namespace Booster.Core.Reports;

public sealed class ReportDocumentBuilder(OfficialSourceResolver links, IBoosterConfiguration config)
{
    public ReportDocument Build(SystemReport report, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(culture);
        var current = config.Current;
        return new Composer(report, links, current.Catalog.Services, current.Thresholds.Startup, culture).Compose();
    }

    private sealed class Composer(SystemReport report, OfficialSourceResolver links, ServiceCatalog services, StartupThresholds startup,
        CultureInfo culture)
    {
        private readonly List<ReportSection> _sections = [];
        private List<ReportBlock> _blocks = [];

        private SystemSnapshot Snapshot => report.Snapshot;

        public ReportDocument Compose()
        {
            Header();
            Findings();
            Cpu();
            Gpu();
            Memory();
            Board();
            Os();
            Storage();
            Displays();
            Drivers();
            _sections.AddRange(SystemHealthReport.Build(report, services, startup, culture));
            _sections.Add(PerformanceReport.BuildSeries(report.Benchmark, culture));
            if (PerformanceReport.BuildComparison(report.Benchmark, culture) is { } comparison)
                _sections.Add(comparison);
            Capabilities();
            return new ReportDocument(ReportStrings.ReportTitle, _sections, ReportStrings.ReportFooter);
        }

        private void Header()
        {
            var metadata = report.Metadata;
            Section(ReportSectionIds.Header, ReportStrings.SectionHeader, null, () => Fields(
                (ReportStrings.GeneratedAt, Text(Timestamp(metadata.GeneratedAt))),
                (ReportStrings.SnapshotTakenAt, Text(Timestamp(Snapshot.TakenAt))),
                (ReportStrings.AppVersion, Text(metadata.AppVersion)),
                (ReportStrings.Privileges, Text(metadata.Privileges.IsElevated ? ReportStrings.PrivilegesElevated : ReportStrings.PrivilegesStandard)),
                (ReportStrings.PerformanceLogUser, Text(Bool(metadata.Privileges.IsPerformanceLogUser)))));
        }

        private void Findings() => Section(ReportSectionIds.Findings, ReportStrings.SectionFindings, null, () =>
        {
            if (report.Findings.Count == 0)
            {
                Paragraph(Text(ReportStrings.NoFindings));
                return;
            }

            Table([ReportStrings.ColumnSeverity, ReportStrings.ColumnFinding, ReportStrings.ColumnSubject, ReportStrings.ColumnDetail],
                report.Findings.Select(finding => Row(
                    Text(EnumText.Format(finding.Severity)) with
                    {
                        Tone = finding.Severity == FindingSeverity.Warning ? CellTone.Warning : CellTone.Info,
                    },
                    Text(EnumText.Format(finding.Kind)), Text(finding.Subject), Text(finding.Detail))));
        });

        private void Cpu() => Section(ReportSectionIds.Cpu, ReportStrings.SectionCpu, Snapshot.Hardware.Cpu.Status, () =>
        {
            if (SectionValue(Snapshot.Hardware.Cpu) is { } cpu)
            {
                Table([ReportStrings.CpuModel, ReportStrings.CpuManufacturer, ReportStrings.CpuCores, ReportStrings.CpuLogicalProcessors,
                        ReportStrings.CpuBaseClock],
                    cpu.Packages.Select(package => Row(Text(package.Name), Value(package.Manufacturer, s => s), Value(package.Cores, Number),
                        Value(package.LogicalProcessors, Number), Value(package.BaseClockMhz, mhz => Format(ReportStrings.UnitMhz, Number(mhz))))));
                Fields(
                    (ReportStrings.CpuTopology, Value(cpu.Topology,
                        t => Format(ReportStrings.CpuTopologyFormat, Number(t.PerformanceCores), Number(t.EfficiencyCores)))),
                    (ReportStrings.CpuHybrid, Value(cpu.Topology, t => Bool(t.IsHybrid))));
            }

            Heading(ReportStrings.SensorsAtSnapshot);
            if (SectionValue(Snapshot.Sensors) is not { } survey)
                return;

            Fields([.. SensorFieldFormatter.Cpu(survey.Sensors.Cpu, culture)]);
        });

        private void Gpu() => Section(ReportSectionIds.Gpu, ReportStrings.SectionGpu, Snapshot.Hardware.Gpu.Status, () =>
        {
            if (SectionValue(Snapshot.Hardware.Gpu) is not { } gpu)
                return;

            var settingsShown = new HashSet<string>(StringComparer.Ordinal);
            foreach (var adapter in gpu.Adapters)
            {
                Heading(adapter.Name);
                var fields = new List<(string, ReportCell)>
                {
                    (ReportStrings.GpuVendor, Text(EnumText.Format(adapter.Vendor))),
                    (ReportStrings.GpuKind, Value(adapter.Kind, EnumText.Format)),
                    (ReportStrings.GpuPci, Value(adapter.Pci, p => p.ToString())),
                    (ReportStrings.GpuLuid, Value(adapter.Luid, l => l.ToString())),
                    (ReportStrings.GpuDedicatedMemory, Value(adapter.DedicatedVideoMemoryBytes, Bytes)),
                    (ReportStrings.GpuSharedMemory, Value(adapter.SharedSystemMemoryBytes, Bytes)),
                    (ReportStrings.GpuDriverVersion, Value(adapter.DriverVersion, s => s)),
                    (ReportStrings.GpuDriverDate, Value(adapter.DriverDate, Date)),
                    (ReportStrings.GpuHighPerformanceRank, Value(adapter.HighPerformanceRank, rank => Number(rank + 1))),
                    (ReportStrings.GpuSoftware, Text(Bool(adapter.IsSoftware))),
                };
                if (adapter.MatchNote is { } note)
                    fields.Add((ReportStrings.GpuMatchNote, Text(note) with { Tone = CellTone.Note }));
                Fields([.. fields]);
                AdapterSensors(adapter, settingsShown);
            }

            Fields((ReportStrings.GpuIsHybrid, Value(gpu.IsHybrid, Bool)));
            Heading(ReportStrings.GpuAppPreferences);
            if (SectionValue(gpu.AppPreferences) is { } preferences && NotEmpty(preferences))
            {
                Table([ReportStrings.ColumnApplication, ReportStrings.ColumnPreference],
                    preferences.Select(preference => Row(Text(preference.ExecutablePath), Text(EnumText.Format(preference.Preference)))));
            }
        });

        private void AdapterSensors(GpuAdapterInfo adapter, HashSet<string> settingsShown)
        {
            Heading(ReportStrings.SensorsAtSnapshot);
            if (SectionValue(Snapshot.Sensors) is not { } survey)
                return;

            if (survey.Sensors.Gpus.FirstOrDefault(g => SameAdapter(g.Adapter, adapter)) is not { } reading)
            {
                Paragraph(Text(ReportStrings.SensorsNoAdapterReading) with { Tone = CellTone.Unavailable });
                return;
            }

            Fields([.. SensorFieldFormatter.Gpu(reading, culture)]);

            if (settingsShown.Add(reading.ProviderId))
                DriverSettings(survey, reading.ProviderId);
        }

        private void DriverSettings(SensorSurvey survey, string providerId)
        {
            var results = survey.Settings.Where(s => s.ProviderId == providerId).ToList();
            if (results.FirstOrDefault(s => s.Application is null) is not { } global)
                return;

            Heading(ReportStrings.GpuSettingsGlobal);
            SettingsTable(global);
            foreach (var application in results.Where(s => s.Application is not null))
            {
                Heading(Format(ReportStrings.GpuSettingsApplicationFormat, application.Application!));
                if (application.Settings.IsAvailable && application.ProfileName is null)
                    Paragraph(Text(ReportStrings.GpuSettingsNoProfile));
                else
                    SettingsTable(application);
            }
        }

        private void SettingsTable(GpuSettingsResult result)
        {
            if (SectionValue(result.Settings) is not { } settings)
                return;
            if (result.ProfileName is { } profile)
                Paragraph(Text(Format(ReportStrings.GpuSettingsProfileFormat, profile)));
            if (!NotEmpty(settings))
                return;

            Table([ReportStrings.ColumnSetting, ReportStrings.ColumnValue, ReportStrings.ColumnOrigin],
                settings.Select(setting => Row(Text(setting.DisplayName) with { Tooltip = setting.Id }, Value(setting.CurrentValue, s => s),
                    Text(EnumText.Format(setting.Origin)))));
        }

        private static bool SameAdapter(GpuAdapterInfo left, GpuAdapterInfo right) =>
            left.Luid.IsAvailable && right.Luid.IsAvailable ? left.Luid.Value == right.Luid.Value : left.Name == right.Name;

        private void Memory() => Section(ReportSectionIds.Memory, ReportStrings.SectionMemory, Snapshot.Hardware.Memory.Status, () =>
        {
            if (SectionValue(Snapshot.Hardware.Memory) is not { } memory)
                return;

            Fields(
                (ReportStrings.MemoryTotal, Value(memory.TotalPhysicalBytes, Bytes)),
                (ReportStrings.MemorySlots, Value(memory.SlotCount, Number)),
                (ReportStrings.MemoryDualChannel, Value(memory.DualChannel, Bool)),
                (ReportStrings.MemoryChannelHint, Text(EnumText.Format(memory.ChannelHint))));
            if (NotEmpty(memory.Modules))
            {
                Table([ReportStrings.ColumnSlot, ReportStrings.ColumnBank, ReportStrings.ColumnCapacity, ReportStrings.ColumnMemoryType,
                        ReportStrings.ColumnRatedSpeed, ReportStrings.ColumnConfiguredSpeed, ReportStrings.ColumnManufacturer,
                        ReportStrings.ColumnPartNumber],
                    memory.Modules.Select(module => Row(Value(module.Locator, s => s), Value(module.BankLabel, s => s),
                        Value(module.CapacityBytes, Bytes), Value(module.MemoryType, s => s), Value(module.RatedSpeedMts, Mts),
                        Value(module.ConfiguredSpeedMts, Mts), Value(module.Manufacturer, s => s), Value(module.PartNumber, s => s))));
            }
        });

        private void Board() => Section(ReportSectionIds.Board, ReportStrings.SectionBoard, Snapshot.Hardware.Board.Status, () =>
        {
            if (SectionValue(Snapshot.Hardware.Board) is not { } board)
                return;

            Fields(
                (ReportStrings.BoardSystemManufacturer, Value(board.SystemManufacturer, s => s)),
                (ReportStrings.BoardSystemModel, Value(board.SystemModel, s => s)),
                (ReportStrings.BoardFormFactor, Value(board.FormFactor, EnumText.Format)),
                (ReportStrings.BoardManufacturer, Value(board.BoardManufacturer, s => s)),
                (ReportStrings.BoardProduct, Value(board.BoardProduct, s => s)),
                (ReportStrings.BiosVendor, Value(board.BiosVendor, s => s)),
                (ReportStrings.BiosVersion, Value(board.BiosVersion, s => s)),
                (ReportStrings.BiosReleaseDate, Value(board.BiosReleaseDate, Date)),
                (ReportStrings.Firmware, Value(board.Firmware, EnumText.Format)));
        });

        private void Os() => Section(ReportSectionIds.Os, ReportStrings.SectionOs, Snapshot.Hardware.Os.Status, () =>
        {
            if (SectionValue(Snapshot.Hardware.Os) is not { } os)
                return;

            Fields(
                (ReportStrings.OsCaption, Value(os.Caption, s => s)),
                (ReportStrings.OsVersion, Value(os.Version, s => s)),
                (ReportStrings.OsDisplayVersion, Value(os.DisplayVersion, s => s)),
                (ReportStrings.OsBuild, Value(os.BuildNumber, b => b.ToString(CultureInfo.InvariantCulture))),
                (ReportStrings.OsUbr, Value(os.Ubr, u => u.ToString(CultureInfo.InvariantCulture))),
                (ReportStrings.OsArchitecture, Value(os.Architecture, s => s)),
                (ReportStrings.OsLastBoot, Value(os.LastBootTime, Timestamp)));
        });

        private void Storage() => Section(ReportSectionIds.Storage, ReportStrings.SectionStorage, Snapshot.Hardware.Storage.Status, () =>
        {
            if (SectionValue(Snapshot.Hardware.Storage) is not { } disks || !NotEmpty(disks))
                return;

            Table([ReportStrings.ColumnDisk, ReportStrings.ColumnMedia, ReportStrings.ColumnBus, ReportStrings.ColumnSize, ReportStrings.ColumnHealth],
                disks.Select(disk => Row(Value(disk.FriendlyName, s => s), Value(disk.Media, EnumText.Format), Value(disk.Bus, EnumText.Format),
                    Value(disk.SizeBytes, Bytes), Value(disk.Health, EnumText.Format))));

            var volumes = disks.SelectMany(d => d.Volumes.Select(v => (Disk: d, Volume: v))).ToList();
            if (volumes.Count > 0)
            {
                Table([ReportStrings.ColumnVolume, ReportStrings.ColumnDisk, ReportStrings.ColumnFileSystem, ReportStrings.ColumnSize,
                        ReportStrings.ColumnFree],
                    volumes.Select(v => Row(Text(v.Volume.DriveLetter), Value(v.Disk.FriendlyName, s => s), Value(v.Volume.FileSystem, s => s),
                        Value(v.Volume.SizeBytes, Bytes), ReportFormat.FreeSpace(v.Volume, culture))));
            }
        });

        private void Displays() => Section(ReportSectionIds.Displays, ReportStrings.SectionDisplays, Snapshot.Hardware.Displays.Status, () =>
        {
            if (SectionValue(Snapshot.Hardware.Displays) is not { } displays || !NotEmpty(displays))
                return;

            Table([ReportStrings.ColumnMonitor, ReportStrings.ColumnResolution, ReportStrings.ColumnRefreshRate, ReportStrings.ColumnHdrSupported,
                    ReportStrings.ColumnHdrEnabled, ReportStrings.ColumnVrr, ReportStrings.ColumnAdapter, ReportStrings.ColumnPrimary],
                displays.Select(display =>
                {
                    var resolution = display.Width.IsAvailable && display.Height.IsAvailable
                        ? Text(Format(ReportStrings.ResolutionFormat, Number(display.Width.Value), Number(display.Height.Value)))
                        : Value(display.Width.IsAvailable ? display.Height : display.Width, Number);
                    return Row(Value(display.MonitorName, s => s), resolution,
                        Value(display.RefreshRateHz, hz => Format(ReportStrings.UnitHz, hz.ToString("0.###", culture))),
                        Value(display.HdrSupported, Bool), Value(display.HdrEnabled, Bool), Value(display.VrrEnabled, Bool),
                        Adapter(display.AdapterLuid), Text(Bool(display.IsPrimary)));
                }));
        });

        private void Drivers() => Section(ReportSectionIds.Drivers, ReportStrings.SectionDrivers, Snapshot.Drivers.Status, () =>
        {
            if (SectionValue(Snapshot.Drivers) is not { } drivers || !NotEmpty(drivers.Devices))
                return;

            var board = Snapshot.Hardware.Board is { IsAvailable: true, Value: { } b } ? b : null;
            Table([ReportStrings.ColumnDevice, ReportStrings.ColumnClass, ReportStrings.ColumnProvider, ReportStrings.ColumnVersion,
                    ReportStrings.ColumnDate, ReportStrings.ColumnSigned, ReportStrings.ColumnState, ReportStrings.ColumnLinks],
                drivers.Devices.Select(device =>
                {
                    var name = device.DeviceName is { IsAvailable: true, Value: { Length: > 0 } deviceName }
                        ? Text(deviceName) with { Tooltip = device.DeviceInstanceId }
                        : Text(device.DeviceInstanceId);
                    return Row(name, Value(device.DeviceClass, s => s), Value(device.DriverProvider, s => s),
                            Value(device.DriverVersion, s => s), Value(device.DriverDate, Date), Value(device.IsSigned, Bool),
                            DeviceStateCell(device), Text(null) with { Links = links.ForDevice(device, board) })
                        with { Flagged = FindingsBuilder.ForDevice(device).Count > 0 };
                }));
        });

        private ReportCell DeviceStateCell(DeviceDriverInfo device)
        {
            if (!device.State.IsAvailable)
                return Value(device.State, EnumText.Format);

            var state = device.State.Value;
            var text = device.ProblemCode is { IsAvailable: true, Value: var code and not 0 }
                ? Format(ReportStrings.DeviceStateWithCode, EnumText.Format(state), Number(code))
                : EnumText.Format(state);
            return Text(text) with { Tone = state == DeviceState.Error ? CellTone.Warning : CellTone.Normal };
        }

        private void Capabilities() => Section(ReportSectionIds.Capabilities, ReportStrings.SectionCapabilities, null, () =>
        {
            if (Snapshot.Capabilities.Count == 0)
            {
                Paragraph(Text(ReportStrings.NoCapabilities));
                return;
            }

            Table([ReportStrings.ColumnCapability, ReportStrings.ColumnScope, ReportStrings.ColumnState, ReportStrings.ColumnCapabilityProvider,
                    ReportStrings.ColumnNote],
                Snapshot.Capabilities.Select(capability => Row(Text(capability.CapabilityId), Text(capability.Scope),
                    Text(EnumText.Format(capability.Status)), Text(capability.ProviderId), Text(capability.Note))));
        });

        private ReportCell Adapter(Reading<AdapterLuid> luid)
        {
            if (luid.IsAvailable && Snapshot.Hardware.Gpu is { IsAvailable: true, Value: { } gpu }
                && gpu.Adapters.FirstOrDefault(a => a.Luid.IsAvailable && a.Luid.Value == luid.Value) is { } adapter)
                return Text(adapter.Name) with { Tooltip = $"LUID {luid.Value}" };
            return Value(luid, l => l.ToString());
        }

        private void Section(string id, string title, DataStatus? status, Action content)
        {
            _blocks = [];
            content();
            _sections.Add(new ReportSection(id, title, status, _blocks));
        }

        private T? SectionValue<T>(Reading<T> reading)
            where T : class
        {
            if (reading is { IsAvailable: true, Value: { } value })
                return value;
            Paragraph(Value(reading, _ => string.Empty));
            return null;
        }

        private bool NotEmpty<T>(IReadOnlyCollection<T> items)
        {
            if (items.Count > 0)
                return true;
            Paragraph(Text(ReportStrings.EmptyList) with { Tone = CellTone.Unavailable });
            return false;
        }

        private void Heading(string text) => _blocks.Add(new ReportHeading(text));

        private void Paragraph(ReportCell cell) => _blocks.Add(new ReportParagraph(cell));

        private void Fields(params (string Label, ReportCell Value)[] rows) =>
            _blocks.Add(new ReportFieldList(rows.Select(r => new ReportField(r.Label, r.Value)).ToList()));

        private void Table(string[] headers, IEnumerable<ReportRow> rows) => _blocks.Add(new ReportTable(headers, rows.ToList()));

        private static ReportRow Row(params ReportCell[] cells) => new(cells);

        private static ReportCell Text(string? text) => ReportFormat.Text(text);

        private static ReportCell Value<T>(Reading<T> reading, Func<T, string> format) => ReportFormat.Value(reading, format);

        private string Format(string format, params object[] args) => string.Format(culture, format, args);

        private string Number(int value) => value.ToString(culture);

        private string Bytes(long value) => ByteSizeFormatter.Format(value, culture);

        private string Mts(int value) => Format(ReportStrings.UnitMts, Number(value));

        private static ReportCell UnknownCell => ReportFormat.UnknownCell;

        private string Date(DateOnly value) => value.ToString("d", culture);

        private string Timestamp(DateTimeOffset value) => ReportFormat.Timestamp(value, culture);

        private static string Bool(bool value) => ReportFormat.Bool(value);
    }
}
