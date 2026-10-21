using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Core.Resources;

namespace Booster.Core.Reports;

public static class SnapshotSummary
{
    private const string ListSeparator = "; ";

    public static IReadOnlyList<ReportField> Build(SystemSnapshot snapshot, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(culture);

        var hardware = snapshot.Hardware;
        return
        [
            Field(ReportStrings.SectionCpu, hardware.Cpu, cpu => Text(Join(cpu.Packages.Select(p => p.Name)))),
            Field(ReportStrings.SectionGpu, hardware.Gpu, gpu => Text(Join(gpu.Adapters.Where(a => !a.IsSoftware).Select(a => a.Name)))),
            Field(ReportStrings.SectionMemory, hardware.Memory, memory => Value(memory.TotalPhysicalBytes, b => ByteSizeFormatter.Format(b, culture))),
            Field(ReportStrings.SectionBoard, hardware.Board, Board),
            Field(ReportStrings.SectionOs, hardware.Os, Os),
            Field(ReportStrings.SectionStorage, hardware.Storage, disks => Text(string.Format(culture, ReportStrings.SummaryStorage,
                disks.Count, disks.Sum(d => d.Volumes.Count)))),
            Field(ReportStrings.SectionDisplays, hardware.Displays,
                displays => Text(Join(displays.Select(d => ReadingFormatter.Format(d.MonitorName, s => s))))),
            Field(ReportStrings.SectionDrivers, snapshot.Drivers, drivers =>
            {
                var flagged = drivers.Devices.Count(d => FindingsBuilder.ForDevice(d).Count > 0);
                return new ReportCell(string.Format(culture, ReportStrings.SummaryDrivers, drivers.Devices.Count, flagged),
                    Tone: flagged > 0 ? CellTone.Warning : CellTone.Normal);
            }),
        ];
    }

    private static ReportField Field<T>(string title, Reading<T> section, Func<T, ReportCell> summarize)
        where T : class
    {
        var source = ReadingFormatter.SourceText(section.Source, section.Note);
        if (section is not { IsAvailable: true, Value: { } value })
            return new ReportField(title, new ReportCell(ReadingFormatter.StatusText(section.Status), source, CellTone.Unavailable));

        var cell = summarize(value);
        if (cell.Text.Length == 0)
            cell = new ReportCell(ReportStrings.EmptyList, Tone: CellTone.Unavailable);
        return new ReportField(title, cell with { Tooltip = cell.Tooltip ?? source });
    }

    private static ReportCell Board(SystemBoardInfo board)
    {
        var text = Join([Known(board.BoardManufacturer), Known(board.BoardProduct)], " ");
        return Text(text.Length > 0 ? text : Join([Known(board.SystemManufacturer), Known(board.SystemModel)], " "));
    }

    private static ReportCell Os(OsInfo os)
    {
        var build = os.BuildNumber is { IsAvailable: true, Value: var number }
            ? os.Ubr is { IsAvailable: true, Value: var ubr }
                ? string.Create(CultureInfo.InvariantCulture, $"{number}.{ubr}")
                : number.ToString(CultureInfo.InvariantCulture)
            : null;
        return Text(Join([Known(os.Caption), Known(os.DisplayVersion), build], " "));
    }

    private static string? Known(Reading<string> reading) => reading.IsAvailable ? reading.Value : null;

    private static string Join(IEnumerable<string?> items, string separator = ListSeparator) =>
        string.Join(separator, items.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));

    private static ReportCell Text(string text) => new(text);

    private static ReportCell Value<T>(Reading<T> reading, Func<T, string> format) => new(
        ReadingFormatter.Format(reading, format),
        ReadingFormatter.SourceText(reading.Source, reading.Note),
        reading.IsAvailable ? CellTone.Normal : CellTone.Unavailable);
}
