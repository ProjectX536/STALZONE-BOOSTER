using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Reports;

namespace Booster.Core.SystemHealth;

/// <param name="Last">Время самого нового события этого вида.</param>
/// <param name="Subjects">До пяти самых частых значений Subject.</param>
public sealed record StabilityKindSummary(StabilityEventKind Kind, int Count, DateTimeOffset Last, IReadOnlyList<string> Subjects);

/// <summary>
/// Сводка журнала стабильности и рекомендации. Всё — только вручную (L3): причины сбоев Booster не устраняет,
/// а лишь показывает, что проверить.
/// </summary>
public static class StabilityAnalyzer
{
    private const int TopSubjects = 5;
    private const string ListSeparator = ", ";
    private const string NoCodes = "—";
    private const string ExecutableExtension = ".exe";

    /// <summary>По убыванию числа; Subjects — до 5 самых частых, без пустых.</summary>
    public static IReadOnlyList<StabilityKindSummary> Summarize(StabilityLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        // OrderByDescending устойчива: при равном числе остаётся порядок появления (журнал — от новых к старым).
        return log.Events
            .GroupBy(e => e.Kind)
            .Select(g => new StabilityKindSummary(g.Key, g.Count(), g.Max(e => e.Time), TopOf(g)))
            .OrderByDescending(s => s.Count)
            .ToArray();
    }

    public static IReadOnlyList<SystemAdvice> Advise(StabilityLog log, IReadOnlyList<string> gameExecutables)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(gameExecutables);

        var advice = new List<SystemAdvice>();
        AddCount(advice, log, AdviceId.UnexpectedShutdowns, StabilityEventKind.UnexpectedShutdown);

        var bugChecks = log.Events.Where(e => e.Kind == StabilityEventKind.BugCheck).ToArray();
        if (bugChecks.Length > 0)
        {
            var codes = TopOf(bugChecks);
            advice.Add(Advice(AdviceId.BugChecks, Count(bugChecks.Length), codes.Count > 0 ? string.Join(ListSeparator, codes) : NoCodes));
        }

        AddCount(advice, log, AdviceId.DisplayDriverResets, StabilityEventKind.DisplayDriverReset);
        AddCount(advice, log, AdviceId.HardwareErrors, StabilityEventKind.HardwareError);
        AddCount(advice, log, AdviceId.DiskErrors, StabilityEventKind.DiskError, StabilityEventKind.FileSystemError);
        AddGameCrashes(advice, log, gameExecutables);
        return advice;
    }

    private static void AddCount(List<SystemAdvice> advice, StabilityLog log, AdviceId id, params StabilityEventKind[] kinds)
    {
        var count = log.Events.Count(e => kinds.Contains(e.Kind));
        if (count > 0)
            advice.Add(Advice(id, Count(count)));
    }

    private static void AddGameCrashes(List<SystemAdvice> advice, StabilityLog log, IReadOnlyList<string> gameExecutables)
    {
        var games = gameExecutables
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => (Display: Path.GetFileName(g.Trim()), Key: ExecutableKey(g)))
            .DistinctBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (games.Length == 0)
            return;

        var crashes = log.Events
            .Where(e => e.Kind is StabilityEventKind.ApplicationCrash or StabilityEventKind.ApplicationHang && e.Subject is not null)
            .Select(e => ExecutableKey(e.Subject!))
            .Where(key => games.Any(g => string.Equals(g.Key, key, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (crashes.Length == 0)
            return;

        var crashed = games.Where(g => crashes.Contains(g.Key, StringComparer.OrdinalIgnoreCase)).Select(g => g.Display);
        advice.Add(Advice(AdviceId.GameCrashes, string.Join(ListSeparator, crashed), Count(crashes.Length)));
    }

    /// <summary>«C:\Games\Game.EXE», «game.exe» и «game» дают один ключ «game» (сравнение — без учёта регистра).</summary>
    private static string ExecutableKey(string name)
    {
        var file = Path.GetFileName(name.Trim());
        return file.EndsWith(ExecutableExtension, StringComparison.OrdinalIgnoreCase) ? file[..^ExecutableExtension.Length] : file;
    }

    private static IReadOnlyList<string> TopOf(IEnumerable<StabilityEvent> events) => events
        .Select(e => e.Subject?.Trim())
        .OfType<string>()
        .Where(s => s.Length > 0)
        .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(g => g.Count())
        .Take(TopSubjects)
        .Select(g => g.First())
        .ToArray();

    private static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);

    private static SystemAdvice Advice(AdviceId id, params string[] arguments) =>
        new(id, AdviceModule.Stability, FindingSeverity.Warning, SafetyLevel.L3ManualOnly, arguments);
}
