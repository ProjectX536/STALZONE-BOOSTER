using System.Globalization;
using Booster.Core.Abstractions;

namespace Booster.Platform.Windows.Monitoring;

public sealed record StabilityRecord(string Provider, int Id, byte? Level, DateTimeOffset Time, string? Subject);

public static class StabilityQueries
{
    public const string SystemLog = "System";
    public const string ApplicationLog = "Application";

    public const int MaxSubjectLength = 200;

    private sealed record Rule(string Provider, int[] Ids, StabilityEventKind Kind, bool ErrorLevelsOnly = false);

    // WHEA пишет и исправленные ошибки уровня Information (4) — это штатная работа, не сбой.
    private static readonly byte[] ErrorLevels = [1, 2, 3];

    private static readonly Rule[] SystemRules =
    [
        new("Microsoft-Windows-Kernel-Power", [41], StabilityEventKind.UnexpectedShutdown),
        new("Microsoft-Windows-WER-SystemErrorReporting", [1001], StabilityEventKind.BugCheck),
        new("Display", [4101], StabilityEventKind.DisplayDriverReset),
        new("Microsoft-Windows-WHEA-Logger", [], StabilityEventKind.HardwareError, ErrorLevelsOnly: true),
        new("disk", [7, 11, 51, 153], StabilityEventKind.DiskError),
        new("stornvme", [129], StabilityEventKind.DiskError),
        new("storahci", [129], StabilityEventKind.DiskError),
        new("Microsoft-Windows-Ntfs", [55], StabilityEventKind.FileSystemError),
    ];

    private static readonly Rule[] ApplicationRules =
    [
        new("Application Error", [1000], StabilityEventKind.ApplicationCrash),
        new("Application Hang", [1002], StabilityEventKind.ApplicationHang),
    ];

    public static string SystemXPath(DateTimeOffset since) => XPath(since, SystemRules);

    public static string ApplicationXPath(DateTimeOffset since) => XPath(since, ApplicationRules);

    public static StabilityEventKind? Classify(string provider, int id, byte? level)
    {
        ArgumentNullException.ThrowIfNull(provider);

        foreach (var rule in SystemRules.Concat(ApplicationRules))
        {
            if (!string.Equals(rule.Provider, provider, StringComparison.OrdinalIgnoreCase))
                continue;
            if (rule.Ids.Length > 0 && !rule.Ids.Contains(id))
                continue;
            if (rule.ErrorLevelsOnly && (level is not { } l || !ErrorLevels.Contains(l)))
                continue;
            return rule.Kind;
        }
        return null;
    }

    public static StabilityEvent? ToEvent(StabilityRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (Classify(record.Provider, record.Id, record.Level) is not { } kind)
            return null;

        var subject = record.Subject?.Trim();
        if (string.IsNullOrEmpty(subject))
            subject = null;
        else if (kind == StabilityEventKind.BugCheck)
            subject = BugCheckCode(subject);

        if (subject is { Length: > MaxSubjectLength })
            subject = subject[..MaxSubjectLength];
        return new StabilityEvent(kind, record.Time, record.Provider, record.Id, subject);
    }

    private static string BugCheckCode(string text)
    {
        var end = text.IndexOfAny([' ', '(']);
        return end > 0 ? text[..end] : text;
    }

    private static string XPath(DateTimeOffset since, IReadOnlyList<Rule> rules)
    {
        var time = since.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var conditions = string.Join(" or ", rules.Select(Condition));
        return $"*[System[TimeCreated[@SystemTime>='{time}'] and ({conditions})]]";
    }

    private static string Condition(Rule rule)
    {
        var filter = rule.ErrorLevelsOnly
            ? string.Join(" or ", ErrorLevels.Select(l => $"Level={l}"))
            : string.Join(" or ", rule.Ids.Select(id => $"EventID={id}"));
        return $"(Provider[@Name='{rule.Provider}'] and ({filter}))";
    }
}
