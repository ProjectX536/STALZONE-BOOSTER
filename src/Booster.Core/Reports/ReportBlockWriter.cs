namespace Booster.Core.Reports;

/// <summary>Блоки раздела по порядку: заголовки, абзацы, списки полей и таблицы.</summary>
internal sealed class ReportBlockWriter
{
    private readonly List<ReportBlock> _blocks = [];

    public IReadOnlyList<ReportBlock> Blocks => _blocks;

    public void Heading(string text) => _blocks.Add(new ReportHeading(text));

    public void Paragraph(ReportCell cell) => _blocks.Add(new ReportParagraph(cell));

    public void Fields(IEnumerable<(string Label, ReportCell Value)> fields) =>
        _blocks.Add(new ReportFieldList([.. fields.Select(f => new ReportField(f.Label, f.Value))]));

    public void Table(IReadOnlyList<string> headers, IEnumerable<ReportRow> rows) => _blocks.Add(new ReportTable(headers, [.. rows]));

    public void Add(IEnumerable<ReportBlock> blocks) => _blocks.AddRange(blocks);
}
