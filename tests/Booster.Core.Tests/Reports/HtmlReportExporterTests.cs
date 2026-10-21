using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Booster.Core.Abstractions;
using Booster.Core.Benchmark;
using Booster.Core.Configuration;
using Booster.Core.Drivers;
using Booster.Core.Inventory;
using Booster.Core.Metrics;
using Booster.Core.Reports;
using Booster.Core.Resources;
using Booster.Core.Tests.TestSupport;
using static Booster.Core.Tests.TestSupport.SnapshotFactory;

namespace Booster.Core.Tests.Reports;

public class HtmlReportExporterTests
{
    private const string NvidiaUrl = "https://www.nvidia.com/en-us/drivers/";
    private const string MsiUrl = "https://www.msi.com/support";

    private static readonly FakeConfiguration Configuration = new(sources: new OfficialSources(
        OfficialSources.CurrentSchemaVersion,
        ["nvidia.com", "msi.com"],
        [new VendorLinkRule("10DE", "NVIDIA — драйверы", NvidiaUrl)],
        [new VendorLinkRule("Micro-Star", "MSI — поддержка", MsiUrl)],
        "ms-settings:windowsupdate"));

    private static readonly OfficialSourceResolver Resolver = new(Configuration);

    private static async Task<string> ExportAsync(SystemReport report)
    {
        using var stream = new MemoryStream();
        await new HtmlReportExporter(new ReportDocumentBuilder(Resolver, Configuration)).ExportAsync(report, stream, TestContext.Current.CancellationToken);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Строка таблицы, в которой есть <paramref name="marker"/>.</summary>
    private static string RowWith(string html, string marker)
    {
        var row = Regex.Matches(html, "<tr>.*?</tr>", RegexOptions.Singleline).Select(m => m.Value)
            .SingleOrDefault(r => r.Contains(marker, StringComparison.Ordinal));
        Assert.NotNull(row);
        return row;
    }

    private static string SectionOf(string html, string id)
    {
        var section = Regex.Match(html, $"<section id=\"{id}\">.*?</section>", RegexOptions.Singleline);
        Assert.True(section.Success, $"no section {id}");
        return section.Value;
    }

    private static BenchmarkReport Benchmark()
    {
        var settings = FakeConfiguration.DefaultThresholds.Comparison;
        var series = SampleSeries.Full();
        var candidate = SampleSeries.Full("b", minutes: 10);
        return new BenchmarkReport(candidate, BenchmarkComparer.Compare(series, candidate, settings), [], settings);
    }

    [Fact]
    public void Format_is_html()
    {
        Assert.Equal("html", new HtmlReportExporter(new ReportDocumentBuilder(Resolver, Configuration)).Format);
    }

    [Fact]
    public async Task Document_is_one_file_with_inline_css_and_no_scripts()
    {
        var html = await ExportAsync(Report());

        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.Contains("<style>", html, StringComparison.Ordinal);
        Assert.Contains("Content-Security-Policy", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("</html>" + Environment.NewLine, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Text_from_the_system_is_escaped()
    {
        var device = Device("<script>alert(1)</script>") with { DriverProvider = Known("\"Vendor\" & Co") };

        var html = await ExportAsync(Report(Healthy().WithDevices(device)));

        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&quot;Vendor&quot; &amp; Co", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Finding_text_is_escaped()
    {
        var finding = new Finding(FindingKind.DeviceError, FindingSeverity.Warning, "<b>x</b>", "<img src=x>");

        var html = await ExportAsync(Report(findings: [finding]));

        Assert.Contains("&lt;b&gt;x&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unavailable_values_show_status_text()
    {
        var snapshot = Healthy();
        snapshot = snapshot with
        {
            Hardware = snapshot.Hardware with { Storage = Reading<IReadOnlyList<StorageDeviceInfo>>.NeedsAdmin("MSFT_PhysicalDisk") },
        };

        var html = await ExportAsync(Report(snapshot));

        // Режим каналов памяти в исправном снимке Unknown.
        Assert.Contains("Неизвестно", html, StringComparison.Ordinal);
        Assert.Contains("Нужны права администратора", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Source_of_a_value_is_shown_as_tooltip()
    {
        var snapshot = Healthy();
        snapshot = snapshot with
        {
            Hardware = snapshot.Hardware with { Os = Reading<OsInfo>.Failure("WMI Win32_OperatingSystem", "access denied") },
        };

        var html = await ExportAsync(Report(snapshot));

        Assert.Contains("title=\"WMI Win32_OperatingSystem: access denied\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Display_row_names_its_adapter_by_luid()
    {
        var html = await ExportAsync(Report());

        Assert.Contains(GpuName, RowWith(html, MonitorName), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Display_on_unknown_adapter_shows_luid()
    {
        var snapshot = Healthy();
        var display = snapshot.Hardware.Displays.Value![0] with { AdapterLuid = Known(new AdapterLuid(0x99, 0)) };
        snapshot = snapshot with { Hardware = snapshot.Hardware with { Displays = Known<IReadOnlyList<DisplayInfo>>([display]) } };

        var row = RowWith(await ExportAsync(Report(snapshot)), MonitorName);

        Assert.Contains("0x00000000_0x00000099", row, StringComparison.Ordinal);
        Assert.DoesNotContain(GpuName, row, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_official_driver_links_point_to_the_web()
    {
        var html = await ExportAsync(Report());

        var urls = Regex.Matches(html, @"https?://", RegexOptions.IgnoreCase);
        Assert.NotEmpty(urls);
        foreach (Match url in urls)
        {
            var anchor = Regex.Match(html[..url.Index], "<a href=\"$");
            Assert.True(anchor.Success, $"URL at {url.Index} is not an <a href>");
            var href = html[url.Index..html.IndexOf('"', url.Index)];
            Assert.True(Resolver.IsAllowed(new Uri(href)), href);
        }

        Assert.Contains($"<a href=\"{NvidiaUrl}\">", html, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"{MsiUrl}\">", html, StringComparison.Ordinal);
        foreach (var resource in new[] { "src=", "<link", "url(", "@import", "<iframe", "<object" })
            Assert.DoesNotContain(resource, html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Html_contains_benchmark_and_comparison_sections()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        string html;
        try
        {
            html = await ExportAsync(Report(benchmark: Benchmark()));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        var benchmark = SectionOf(html, "benchmark");
        Assert.Contains("game.exe", benchmark, StringComparison.Ordinal);
        Assert.Contains("80,5", benchmark, StringComparison.Ordinal);
        Assert.Contains("Недостаточно данных", benchmark, StringComparison.Ordinal);
        Assert.Contains(ReportStrings.BenchmarkRecommendationsNote, benchmark, StringComparison.Ordinal);
        Assert.Contains(ReportStrings.ComparisonChange, SectionOf(html, "comparison"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Html_contains_new_section_ids()
    {
        var html = await ExportAsync(Report());

        foreach (var id in new[] { "systemMemory", "services", "stability", "network" })
            SectionOf(html, id);
        Assert.Contains(WebUtility.HtmlEncode(ReportStrings.NetworkCaveat), SectionOf(html, "network"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Html_without_series_has_no_comparison_section()
    {
        var html = await ExportAsync(Report());

        Assert.Contains(WebUtility.HtmlEncode(ReportStrings.BenchmarkNoSeries), SectionOf(html, "benchmark"), StringComparison.Ordinal);
        Assert.DoesNotContain("<section id=\"comparison\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_without_findings_says_so()
    {
        Assert.Contains("Замечаний нет", await ExportAsync(Report()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Output_stream_stays_open()
    {
        using var stream = new MemoryStream();

        await new HtmlReportExporter(new ReportDocumentBuilder(Resolver, Configuration)).ExportAsync(Report(), stream, TestContext.Current.CancellationToken);

        Assert.True(stream.CanWrite);
    }
}
