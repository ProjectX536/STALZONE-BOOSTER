using Booster.Core.Abstractions;
using Booster.Core.Drivers;

namespace Booster.Core.Reports;

/// <summary>Вид ячейки: в HTML — класс CSS, в окне приложения — цвет и начертание.</summary>
public enum CellTone
{
    Normal,

    /// <summary>Значение не получено: показан статус («Неизвестно», ...), а не догадка.</summary>
    Unavailable,

    Warning,
    Info,
    Note,
}

/// <summary>
/// Ячейка: текст и подсказка — источник значения. У ячейки ссылок <see cref="Text"/> пуст, а ссылки — только
/// официальные источники от <see cref="OfficialSourceResolver"/>.
/// </summary>
public sealed record ReportCell(string Text, string? Tooltip = null, CellTone Tone = CellTone.Normal)
{
    public IReadOnlyList<OfficialLink> Links { get; init; } = [];
}

public abstract record ReportBlock;

public sealed record ReportHeading(string Text) : ReportBlock;

public sealed record ReportParagraph(ReportCell Content) : ReportBlock;

public sealed record ReportField(string Label, ReportCell Value);

public sealed record ReportFieldList(IReadOnlyList<ReportField> Fields) : ReportBlock;

/// <summary><see cref="Flagged"/> — у строки есть замечание; сейчас так отмечаются устройства в таблице драйверов.</summary>
public sealed record ReportRow(IReadOnlyList<ReportCell> Cells, bool Flagged = false);

public sealed record ReportTable(IReadOnlyList<string> Headers, IReadOnlyList<ReportRow> Rows) : ReportBlock;

/// <summary><see cref="Status"/> — статус данных раздела; null у разделов без своего источника (шапка, замечания, замер).</summary>
public sealed record ReportSection(string Id, string Title, DataStatus? Status, IReadOnlyList<ReportBlock> Blocks);

/// <summary>Отчёт без разметки: его показывают и HTML-файл, и окна приложения.</summary>
public sealed record ReportDocument(string Title, IReadOnlyList<ReportSection> Sections, string Footer)
{
    public ReportSection? Section(string id) => Sections.FirstOrDefault(s => s.Id == id);
}

/// <summary>Идентификаторы разделов; в HTML это id элементов section.</summary>
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
