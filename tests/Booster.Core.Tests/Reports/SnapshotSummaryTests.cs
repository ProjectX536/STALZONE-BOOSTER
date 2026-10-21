using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Core.Reports;
using Booster.Core.Resources;
using static Booster.Core.Tests.TestSupport.SnapshotFactory;

namespace Booster.Core.Tests.Reports;

public class SnapshotSummaryTests
{
    private static readonly CultureInfo Russian = new("ru-RU");

    private static ReportCell Line(SystemSnapshot snapshot, string title) =>
        Assert.Single(SnapshotSummary.Build(snapshot, Russian), f => f.Label == title).Value;

    [Fact]
    public void One_line_per_data_section()
    {
        Assert.Equal(
            [ReportStrings.SectionCpu, ReportStrings.SectionGpu, ReportStrings.SectionMemory, ReportStrings.SectionBoard,
                ReportStrings.SectionOs, ReportStrings.SectionStorage, ReportStrings.SectionDisplays, ReportStrings.SectionDrivers],
            SnapshotSummary.Build(Healthy(), Russian).Select(f => f.Label));
    }

    [Fact]
    public void Healthy_snapshot_is_summarized_from_known_values_with_source()
    {
        var snapshot = Healthy();

        Assert.Equal("AMD Ryzen 7 5800X 8-Core Processor", Line(snapshot, ReportStrings.SectionCpu).Text);
        Assert.Equal(GpuName, Line(snapshot, ReportStrings.SectionGpu).Text);
        Assert.Equal("Micro-Star International Co., Ltd. B550-A PRO (MS-7C56)", Line(snapshot, ReportStrings.SectionBoard).Text);
        Assert.Equal("Microsoft Windows 11 Pro 25H2 26300.1000", Line(snapshot, ReportStrings.SectionOs).Text);
        Assert.Equal("Дисков: 1, томов: 1", Line(snapshot, ReportStrings.SectionStorage).Text);
        Assert.Equal(MonitorName, Line(snapshot, ReportStrings.SectionDisplays).Text);
        Assert.Equal(Source, Line(snapshot, ReportStrings.SectionCpu).Tooltip);
    }

    [Fact]
    public void Section_without_data_shows_its_status_and_source()
    {
        var snapshot = Healthy() with
        {
            Hardware = Healthy().Hardware with { Os = Reading<OsInfo>.Failure("WMI Win32_OperatingSystem", "access denied") },
        };

        var line = Line(snapshot, ReportStrings.SectionOs);

        Assert.Equal(CellTone.Unavailable, line.Tone);
        Assert.Equal("WMI Win32_OperatingSystem: access denied", line.Tooltip);
        Assert.Equal(ReadingFormatter.StatusText(DataStatus.Failed), line.Text);
    }

    [Fact]
    public void Devices_with_findings_are_counted_and_highlighted()
    {
        var snapshot = Healthy().WithDevices(Device("Рабочее"), Device("Сломанное") with { State = Known(DeviceState.Error) });

        var line = Line(snapshot, ReportStrings.SectionDrivers);

        Assert.Equal("Устройств: 2, с замечаниями: 1", line.Text);
        Assert.Equal(CellTone.Warning, line.Tone);
    }

    [Fact]
    public void Empty_list_is_said_to_be_empty()
    {
        var snapshot = Healthy() with { Hardware = Healthy().Hardware with { Displays = Known<IReadOnlyList<DisplayInfo>>([]) } };

        var line = Line(snapshot, ReportStrings.SectionDisplays);

        Assert.Equal(ReportStrings.EmptyList, line.Text);
        Assert.Equal(CellTone.Unavailable, line.Tone);
    }

    [Fact]
    public void Unknown_total_memory_shows_its_status_instead_of_a_number()
    {
        var memory = Healthy().Hardware.Memory.Value! with { TotalPhysicalBytes = Reading<long>.Unknown("WMI", "no value") };
        var snapshot = Healthy() with { Hardware = Healthy().Hardware with { Memory = Known(memory) } };

        var line = Line(snapshot, ReportStrings.SectionMemory);

        Assert.Equal(ReadingFormatter.StatusText(DataStatus.Unknown), line.Text);
        Assert.Equal(CellTone.Unavailable, line.Tone);
        Assert.Equal("WMI: no value", line.Tooltip);
    }
}
