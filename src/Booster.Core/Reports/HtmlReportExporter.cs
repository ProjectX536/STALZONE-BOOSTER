using System.Globalization;
using System.Net;
using System.Text;
using Booster.Core.Abstractions;
using Booster.Core.Resources;

namespace Booster.Core.Reports;

public sealed class HtmlReportExporter(ReportDocumentBuilder documents) : IReportExporter
{
    private const string Css = """
        body { font: 14px/1.45 "Segoe UI", system-ui, sans-serif; color: #1f1f1f; background: #fff; max-width: 1200px; margin: 24px auto; padding: 0 16px; }
        h1 { font-size: 1.6em; margin: 0 0 .4em; }
        h2 { font-size: 1.25em; margin: 1.6em 0 .5em; padding-bottom: .2em; border-bottom: 1px solid #d0d0d0; }
        h3 { font-size: 1.05em; margin: 1.2em 0 .4em; }
        .scroll { overflow-x: auto; }
        table { border-collapse: collapse; margin: .4em 0 1em; }
        th, td { border: 1px solid #d6d6d6; padding: 4px 8px; text-align: left; vertical-align: top; }
        thead th { background: #f2f2f2; }
        table.fields th { background: #f7f7f7; font-weight: 600; min-width: 14em; }
        td[title] { cursor: help; }
        .na { color: #6b6b6b; font-style: italic; }
        .warning { color: #a4262c; font-weight: 600; }
        .info { color: #005a9e; }
        .note, footer { color: #555; }
        footer { margin-top: 2em; font-size: .9em; }
        """;

    public string Format => "html";

    public async Task ExportAsync(SystemReport report, Stream output, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(output);

        var html = Render(documents.Build(report, CultureInfo.CurrentCulture));
        var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
        await using (writer.ConfigureAwait(false))
        {
            await writer.WriteAsync(html.AsMemory(), ct).ConfigureAwait(false);
            await writer.FlushAsync(ct).ConfigureAwait(false);
        }
    }

    private static string Render(ReportDocument document)
    {
        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.Append("<html lang=\"").Append(Encode(ReportStrings.HtmlLanguage)).AppendLine("\">");
        html.AppendLine("<head>");
        html.AppendLine("<meta charset=\"utf-8\">");
        html.AppendLine("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\">");
        html.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.Append("<title>").Append(Encode(document.Title)).AppendLine("</title>");
        html.AppendLine("<style>").AppendLine(Css).AppendLine("</style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.Append("<h1>").Append(Encode(document.Title)).AppendLine("</h1>");

        foreach (var section in document.Sections)
        {
            html.Append("<section id=\"").Append(Encode(section.Id)).AppendLine("\">");
            html.Append("<h2>").Append(Encode(section.Title)).AppendLine("</h2>");
            foreach (var block in section.Blocks)
                AppendBlock(html, block);
            html.AppendLine("</section>");
        }

        html.Append("<footer><p>").Append(Encode(document.Footer)).AppendLine("</p></footer>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");
        return html.ToString();
    }

    private static void AppendBlock(StringBuilder html, ReportBlock block)
    {
        switch (block)
        {
            case ReportHeading heading:
                html.Append("<h3>").Append(Encode(heading.Text)).AppendLine("</h3>");
                break;
            case ReportParagraph paragraph:
                AppendCell(html, "p", paragraph.Content);
                html.AppendLine();
                break;
            case ReportFieldList list:
                html.AppendLine("<table class=\"fields\"><tbody>");
                foreach (var field in list.Fields)
                {
                    html.Append("<tr><th>").Append(Encode(field.Label)).Append("</th>");
                    AppendCell(html, "td", field.Value);
                    html.AppendLine("</tr>");
                }
                html.AppendLine("</tbody></table>");
                break;
            case ReportTable table:
                html.Append("<div class=\"scroll\"><table><thead><tr>");
                foreach (var header in table.Headers)
                    html.Append("<th>").Append(Encode(header)).Append("</th>");
                html.AppendLine("</tr></thead><tbody>");
                foreach (var row in table.Rows)
                {
                    html.Append("<tr>");
                    foreach (var cell in row.Cells)
                        AppendCell(html, "td", cell);
                    html.AppendLine("</tr>");
                }
                html.AppendLine("</tbody></table></div>");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(block), block.GetType().Name, null);
        }
    }

    private static void AppendCell(StringBuilder html, string tag, ReportCell cell)
    {
        html.Append('<').Append(tag);
        if (CssClass(cell.Tone) is { } cssClass)
            html.Append(" class=\"").Append(cssClass).Append('"');
        if (cell.Tooltip is { } tooltip)
            html.Append(" title=\"").Append(Encode(tooltip)).Append('"');
        html.Append('>');
        if (cell.Links.Count > 0)
            html.AppendJoin("<br>", cell.Links.Select(link => $"<a href=\"{Encode(link.Url.AbsoluteUri)}\">{Encode(link.Title)}</a>"));
        else
            html.Append(Encode(cell.Text));
        html.Append("</").Append(tag).Append('>');
    }

    private static string? CssClass(CellTone tone) => tone switch
    {
        CellTone.Unavailable => "na",
        CellTone.Warning => "warning",
        CellTone.Info => "info",
        CellTone.Note => "note",
        _ => null,
    };

    private static string Encode(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
}
