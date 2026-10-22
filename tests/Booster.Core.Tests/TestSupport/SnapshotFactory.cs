using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Hags;
using Booster.Core.Inventory;
using Booster.Core.Reports;
using Booster.Core.Sensors;
using Booster.Core.SystemHealth;

namespace Booster.Core.Tests.TestSupport;

internal static class SnapshotFactory
{
    public const string Source = "test";
    public const string GpuName = "NVIDIA GeForce RTX 3060 Ti";
    public const string GpuHardwareId = @"PCI\VEN_10DE&DEV_2489&SUBSYS_88AC1462&REV_A1";
    public const string MonitorName = "LG ULTRAGEAR";
    public const long GiB = 1L << 30;

    public static readonly AdapterLuid GpuLuid = new(0x0000C2A1, 0);
    public static readonly DateTimeOffset TakenAt = new(2026, 10, 17, 12, 0, 0, TimeSpan.FromHours(3));

    public static Reading<T> Known<T>(T value) => Reading<T>.Known(value, Source);

    public static SystemSnapshot Healthy() => new(
        TakenAt,
        new PrivilegeState(IsElevated: true, IsPerformanceLogUser: false),
        Hardware(),
        Known(new DriverInventoryResult([
            Device(GpuName, "Display", GpuHardwareId),
            Device("Стандартный контроллер SATA AHCI", "System", @"PCI\VEN_1022&DEV_7901&SUBSYS_7C561462&REV_51"),
        ])),
        [],
        Known(new SensorSurvey(new SensorSnapshot(TakenAt, [], new CpuTelemetry(
            Reading<double>.Unknown(Source), Reading<double>.Unknown(Source), Reading<double>.Unknown(Source),
            Reading<double>.Unknown(Source), Reading<double>.Unknown(Source))), [])),
        SystemSamples.Healthy());

    public static HardwareInventory Hardware() => new(
        TakenAt,
        Known(new CpuInventory(
            [new CpuPackage("AMD Ryzen 7 5800X 8-Core Processor", Known("AuthenticAMD"), Known(8), Known(16), Known(3800))],
            Known(new CoreTopology(8, 0, IsHybrid: false)))),
        Known(new GpuInventory(
            [new GpuAdapterInfo(GpuName, GpuVendor.Nvidia, Known(new PciId(0x10DE, 0x2489, 0x88AC1462, 0xA1)), Known(GpuLuid),
                Known(GpuAdapterKind.Discrete), Known(8 * GiB), Known(16 * GiB), Known("32.0.15.6094"),
                Known(new DateOnly(2024, 8, 14)), Known(0), IsSoftware: false, MatchNote: null)],
            Known(false),
            Known<IReadOnlyList<AppGpuPreference>>([]))),
        Known(new MemoryInventory(Known(32 * GiB),
            [Module("DIMM 0", "P0 CHANNEL A"), Module("DIMM 1", "P0 CHANNEL B")],
            Known(4),
            Reading<bool>.Unknown(Source, "not measured"),
            ChannelLabelHint.DifferentChannelsByLabel)),
        Known(new SystemBoardInfo(Known("Micro-Star International Co., Ltd."), Known("MS-7C56"), Known(PcFormFactor.Desktop),
            Known("Micro-Star International Co., Ltd."), Known("B550-A PRO (MS-7C56)"),
            Known("American Megatrends International, LLC."), Known("A.G0"), Known(new DateOnly(2024, 3, 1)),
            Known(FirmwareKind.Uefi))),
        Known(new OsInfo(Known("Microsoft Windows 11 Pro"), Known("10.0.26300"), Known(26300), Known(1000), Known("25H2"),
            Known("64-bit"), Known(TakenAt.AddHours(-5)))),
        Known<IReadOnlyList<StorageDeviceInfo>>([
            new StorageDeviceInfo(Known("Samsung SSD 980 PRO 1TB"), Known(StorageMediaKind.Ssd), Known(StorageBusKind.Nvme),
                Known(1_000_204_886_016L), Known(StorageHealth.Healthy), [Volume("C:", 500 * GiB, 250 * GiB)]),
        ]),
        Known<IReadOnlyList<DisplayInfo>>([
            new DisplayInfo(Known(MonitorName), Known(2560), Known(1440), Known(143.856), Known(true), Known(false), Known(true),
                Known(GpuLuid), IsPrimary: true),
        ]));

    public static DeviceDriverInfo Device(string name, string deviceClass = "System", string hardwareId = @"ACPI\PNP0A08") => new(
        hardwareId + @"\0",
        Known(name),
        Known(deviceClass),
        [hardwareId],
        [],
        Known("Microsoft"),
        Known("10.0.26300.1000"),
        Known(new DateOnly(2006, 6, 21)),
        Known("machine.inf"),
        Known(true),
        Known("Microsoft Windows"),
        Known(DeviceState.Ok),
        Known(0));

    public static VolumeInfo Volume(string driveLetter, long sizeBytes, long freeBytes) =>
        new(driveLetter, Known("NTFS"), Known(sizeBytes), Known(freeBytes));

    public static SystemSnapshot WithDevices(this SystemSnapshot snapshot, params DeviceDriverInfo[] devices) =>
        snapshot with { Drivers = Known(new DriverInventoryResult(devices)) };

    public static SystemSnapshot WithVolumes(this SystemSnapshot snapshot, params VolumeInfo[] volumes) =>
        snapshot with
        {
            Hardware = snapshot.Hardware with
            {
                Storage = Known<IReadOnlyList<StorageDeviceInfo>>([
                    new StorageDeviceInfo(Known("Samsung SSD 980 PRO 1TB"), Known(StorageMediaKind.Ssd), Known(StorageBusKind.Nvme),
                        Known(1_000_204_886_016L), Known(StorageHealth.Healthy), volumes),
                ]),
            },
        };

    public static SystemSnapshot WithSensors(this SystemSnapshot snapshot, SensorSnapshot sensors, params GpuSettingsResult[] settings) =>
        snapshot with { Sensors = Known(new SensorSurvey(sensors, settings)) };

    public static SystemReport Report(SystemSnapshot? snapshot = null, IReadOnlyList<Finding>? findings = null,
        BenchmarkReport? benchmark = null, IReadOnlyList<SystemAdvice>? advice = null, NetworkTestResult? network = null,
        IReadOnlyList<TweakProposal>? tweaks = null, HagsExperimentView? hags = null, GameNetworkReportView? gameNetwork = null)
    {
        snapshot ??= Healthy();
        return new SystemReport(
            new ReportMetadata(ReportMetadata.CurrentSchemaVersion, "1.0.0-test", TakenAt.AddMinutes(1), snapshot.Privileges),
            snapshot, findings ?? [], benchmark, advice ?? [], network, tweaks ?? [], hags, gameNetwork);
    }

    private static MemoryModule Module(string locator, string bankLabel) => new(Known(16 * GiB), Known("DDR4"), Known(3200), Known(3200),
        Known("Kingston"), Known("KF3200C16D4/16GX"), Known(locator), Known(bankLabel));
}
