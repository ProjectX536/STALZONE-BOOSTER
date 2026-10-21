using Booster.Core.Abstractions;
using Booster.Core.Drivers;

namespace Booster.Core.Reports;

public enum CellTone
{
    Normal,

    Unavailable,

    Warning,
    Info,
    Note,
}

public sealed record ReportCell(string Text, string? Tooltip = null, CellTone Tone = CellTone.Normal)
{
    public IReadOnlyList<OfficialLink> Links { get; init; } = [];
}

public abstract record ReportBlock;

public sealed record ReportHeading(string Text) : ReportBlock;

public sealed record ReportParagraph(ReportCell Content) : ReportBlock;

public sealed record ReportField(string Label, ReportCell Value);

public sealed record ReportFieldList(IReadOnlyList<ReportField> Fields) : ReportBlock;

public sealed record ReportRow(IReadOnlyList<ReportCell> Cells, bool Flagged = false);

public sealed record ReportTable(IReadOnlyList<string> Headers, IReadOnlyList<ReportRow> Rows) : ReportBlock;

public sealed record ReportSection(string Id, string Title, DataStatus? Status, IReadOnlyList<ReportBlock> Blocks);

public sealed record ReportDocument(string Title, IReadOnlyList<ReportSection> Sections, string Footer)
{
    public ReportSection? Section(string id) => Sections.FirstOrDefault(s => s.Id == id);
}

public static class ReportSectionIds
{
    public const string Header = "header";
    public const string Findings = "findings";
    public const string Cpu = "cpu";
    public const string Gpu = "gpu";
    public const string Memory = "memory";
    public const string Board = "board";
    public const string Os = "os";
    public const string Storage = "storage";
    public const string Displays = "displays";
    public const string Drivers = "drivers";
    public const string SystemMemory = "systemMemory";
    public const string Services = "services";
    public const string Stability = "stability";
    public const string Network = "network";
    public const string Startup = "startup";
    public const string Tweaks = "tweaks";
    public const string Latency = "latency";
    public const string Hags = "hags";
    public const string Benchmark = "benchmark";
    public const string Comparison = "comparison";
    public const string Capabilities = "capabilities";
}
