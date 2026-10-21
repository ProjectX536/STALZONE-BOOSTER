using System.Globalization;
using System.Text;
using Booster.Core.Abstractions;

namespace Booster.Platform.Windows.Monitoring;

public sealed record PresentMonCsvParse(FrameCaptureStatus Status, FrameCapture? Capture, string? Detail);

/// <summary>
/// CSV PresentMon → кадры одной swap chain одного процесса (docs/ARCHITECTURE.md, раздел 10).
/// Имена колонок сравниваются без учёта регистра: с <c>--v1_metrics</c> PresentMon пишет <c>msBetweenPresents</c>.
/// Колонки меток и занятости в форматах 2.6, <c>--v2_metrics</c> и <c>--v1_metrics</c> называются по-разному,
/// поэтому ищутся по спискам имён; кадр без них остаётся, нужные поля равны null.
/// </summary>
public static class PresentMonCsvParser
{
    public const string MsBetweenPresents = "MsBetweenPresents";

    /// <summary>Колонка интервала в формате <c>--v2_metrics</c>.</summary>
    public const string FrameTime = "FrameTime";

    public static PresentMonCsvParse Parse(TextReader csv, string? processName, int? processId, string csvPath)
    {
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentNullException.ThrowIfNull(csvPath);
        if ((processName is null) == (processId is null))
            throw new ArgumentException("Exactly one of processName and processId must be set.");

        using var records = ReadRecords(csv).GetEnumerator();
        if (!records.MoveNext())
            return Unsupported("CSV is empty");

        var header = records.Current;
        var application = Column(header, "Application");
        var pid = Column(header, "ProcessID");
        var swapChain = Column(header, "SwapChainAddress");
        if (application < 0 || pid < 0 || swapChain < 0)
            return Unsupported("CSV has no Application, ProcessID or SwapChainAddress column");

        var (interval, intervalColumn) = Column(header, MsBetweenPresents) is >= 0 and var ms
            ? (ms, MsBetweenPresents)
            : (Column(header, FrameTime), FrameTime);
        if (interval < 0)
            return Unsupported($"CSV has neither {MsBetweenPresents} nor {FrameTime} column");
        var columns = new FrameColumns(
            interval,
            Column(header, "FrameType"),
            Column(header, "PresentMode"),
            FirstColumn(header, "TimeInQPC", "CPUStartQPC"),
            FirstColumn(header, "MsCPUBusy", "CPUBusy"),
            FirstColumn(header, "MsGPUBusy", "GPUBusy", "msGPUActive"),
            Column(header, "SyncInterval"));

        var processes = new Dictionary<int, ProcessFrames>();
        var processOrder = new List<ProcessFrames>();
        var targetRows = 0;
        while (records.MoveNext())
        {
            var row = records.Current;
            if (!int.TryParse(Field(row, pid), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                continue;
            var app = Field(row, application);
            if (processId is { } wanted
                    ? id != wanted
                    : !string.Equals(app, processName, StringComparison.OrdinalIgnoreCase))
                continue;

            targetRows++;
            if (!processes.TryGetValue(id, out var process))
            {
                processes.Add(id, process = new ProcessFrames(id, app));
                processOrder.Add(process);
            }
            process.Add(Field(row, swapChain), row, columns);
        }

        var target = processId is { } p ? $"process ID {p}" : $"process {processName}";
        if (targetRows == 0)
            return new(FrameCaptureStatus.TargetNotFound, null, $"CSV has no rows for {target}");

        // Несколько PID — процесс перезапускался; несколько swap chain — оверлей или второе окно. Берётся основной поток кадров.
        var chosen = FirstMax(processOrder, x => x.ValidFrames);
        var chain = FirstMax(chosen.Chains, c => c.Frames.Count);
        if (chain.Frames.Count == 0)
        {
            return new(FrameCaptureStatus.NoFrames, null,
                $"All {targetRows} rows of {target} have no usable {intervalColumn} (NA, empty or not positive)");
        }

        return new(FrameCaptureStatus.Completed,
            new FrameCapture(chosen.Application, chosen.Id, chain.Frames, intervalColumn, chain.Discarded, chain.FrameTypes,
                chain.PresentModes, csvPath),
            null);
    }

    private static PresentMonCsvParse Unsupported(string detail) => new(FrameCaptureStatus.UnsupportedFormat, null, detail);

    private static int Column(List<string> header, string name) =>
        header.FindIndex(h => string.Equals(h.Trim(), name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Первая найденная колонка из списка в порядке приоритета; -1 — нет ни одной.</summary>
    private static int FirstColumn(List<string> header, params string[] names) =>
        names.Select(name => Column(header, name)).FirstOrDefault(index => index >= 0, -1);

    /// <summary>Пусто, если колонки нет (-1) или строка короче заголовка.</summary>
    private static string Field(List<string> row, int index) => index >= 0 && index < row.Count ? row[index] : string.Empty;

    /// <summary>При равенстве — встреченный в файле первым.</summary>
    private static T FirstMax<T>(IReadOnlyList<T> items, Func<T, int> key)
    {
        var best = items[0];
        foreach (var item in items)
        {
            if (key(item) > key(best))
                best = item;
        }
        return best;
    }

    /// <summary>Записи RFC 4180: поля в кавычках могут содержать запятые, переводы строк и удвоенные кавычки; концы строк CR, LF или CRLF.</summary>
    private static IEnumerable<List<string>> ReadRecords(TextReader reader)
    {
        // StreamReader убирает BOM сам, а строка, прочитанная иначе, может начинаться с него.
        if (reader.Peek() == '﻿')
            reader.Read();

        var record = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var recordStarted = false;
        int next;
        while ((next = reader.Read()) >= 0)
        {
            var ch = (char)next;
            if (inQuotes)
            {
                if (ch != '"')
                    field.Append(ch);
                else if (reader.Peek() == '"')
                    field.Append((char)reader.Read());
                else
                    inQuotes = false;
                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    recordStarted = true;
                    break;
                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    recordStarted = true;
                    break;
                case '\r' or '\n':
                    if (ch == '\r' && reader.Peek() == '\n')
                        reader.Read();
                    if (recordStarted)
                    {
                        record.Add(field.ToString());
                        yield return record;
                        record = [];
                        field.Clear();
                        recordStarted = false;
                    }
                    break;
                default:
                    field.Append(ch);
                    recordStarted = true;
                    break;
            }
        }

        if (recordStarted)
        {
            record.Add(field.ToString());
            yield return record;
        }
    }

    private sealed class ProcessFrames(int id, string application)
    {
        private readonly Dictionary<string, ChainFrames> _chains = new(StringComparer.Ordinal);
        private readonly List<ChainFrames> _chainOrder = [];

        public int Id { get; } = id;

        public string Application { get; } = application;

        public int ValidFrames { get; private set; }

        public IReadOnlyList<ChainFrames> Chains => _chainOrder;

        public void Add(string swapChain, List<string> row, FrameColumns columns)
        {
            if (!_chains.TryGetValue(swapChain, out var chain))
            {
                _chains.Add(swapChain, chain = new ChainFrames());
                _chainOrder.Add(chain);
            }
            if (chain.Add(row, columns))
                ValidFrames++;
        }
    }

    /// <summary>Индексы колонок кадра; -1 — колонки нет.</summary>
    private sealed record FrameColumns(int Interval, int FrameType, int PresentMode, int Qpc, int CpuBusy, int GpuBusy, int SyncInterval);

    private sealed class ChainFrames
    {
        private readonly HashSet<string> _seenFrameTypes = new(StringComparer.Ordinal);
        private readonly HashSet<string> _seenPresentModes = new(StringComparer.Ordinal);

        public List<FrameSample> Frames { get; } = [];

        public int Discarded { get; private set; }

        public List<string> FrameTypes { get; } = [];

        public List<string> PresentModes { get; } = [];

        /// <summary>
        /// Интервал NA, пустой, нечисловой или не больше нуля — кадр отбрасывается и считается.
        /// Остальные поля с такими значениями становятся null, кадр остаётся.
        /// </summary>
        public bool Add(List<string> row, FrameColumns columns)
        {
            if (!double.TryParse(Field(row, columns.Interval), NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)
                || !double.IsFinite(ms)
                || ms <= 0)
            {
                Discarded++;
                return false;
            }

            Frames.Add(new FrameSample(
                Ticks(Field(row, columns.Qpc)),
                ms,
                Duration(Field(row, columns.CpuBusy)),
                Duration(Field(row, columns.GpuBusy)),
                int.TryParse(Field(row, columns.SyncInterval), NumberStyles.None, CultureInfo.InvariantCulture, out var sync) ? sync : null));
            Remember(Field(row, columns.FrameType), _seenFrameTypes, FrameTypes);
            Remember(Field(row, columns.PresentMode), _seenPresentModes, PresentModes);
            return true;
        }

        private static long? Ticks(string value) =>
            long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) && ticks > 0 ? ticks : null;

        private static double? Duration(string value) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) && double.IsFinite(ms) && ms >= 0 ? ms : null;

        /// <summary>Различные непустые значения в порядке первого появления.</summary>
        private static void Remember(string value, HashSet<string> seen, List<string> values)
        {
            if (value.Length > 0 && seen.Add(value))
                values.Add(value);
        }
    }
}
