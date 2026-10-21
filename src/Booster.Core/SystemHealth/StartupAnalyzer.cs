using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Inventory;
using Booster.Core.Reports;

namespace Booster.Core.SystemHealth;

public enum StartupVerdict
{
    /// <summary>HKCU Run или папка пользователя, включён, процесс запущен и тяжелее порога — можно отключить с подтверждением.</summary>
    Candidate,

    /// <summary>Тяжёлый, но HKLM или общая папка: только рекомендация (нужны права администратора).</summary>
    NeedsAdmin,

    /// <summary>Защитное ПО или античит по ключевым словам каталога служб — не предлагается.</summary>
    Protected,

    Light,

    NotRunning,

    Disabled,

    /// <summary>RunOnce, exe неизвестен или состояние Unknown.</summary>
    Unknown,
}

public sealed record StartupAssessment(StartupItem Item, StartupVerdict Verdict);

/// <summary>
/// Оценка элементов автозагрузки. Первое совпадение сверху вниз: защита проверяется раньше памяти, поэтому тяжёлый антивирус
/// не предлагается никогда, а элемент, о котором нельзя сказать надёжно, остаётся Unknown.
/// </summary>
public static class StartupAnalyzer
{
    private const double BytesPerMb = 1024 * 1024;
    private const string NameSeparator = ", ";

    public static IReadOnlyList<StartupAssessment> Assess(IReadOnlyList<StartupItem> items, StartupThresholds thresholds,
        ServiceCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentNullException.ThrowIfNull(catalog);

        return items.Select(item => new StartupAssessment(item, Verdict(item, thresholds, catalog))).ToArray();
    }

    /// <summary>Candidate → HeavyStartupItems (Info, L2); NeedsAdmin → HeavyMachineStartupItems (Info, L3); аргументы — имена через «, ».</summary>
    public static IReadOnlyList<SystemAdvice> Advise(IReadOnlyList<StartupAssessment> assessments)
    {
        ArgumentNullException.ThrowIfNull(assessments);

        var advice = new List<SystemAdvice>();
        Add(StartupVerdict.Candidate, AdviceId.HeavyStartupItems, SafetyLevel.L2Confirmed);
        Add(StartupVerdict.NeedsAdmin, AdviceId.HeavyMachineStartupItems, SafetyLevel.L3ManualOnly);
        return advice;

        void Add(StartupVerdict verdict, AdviceId id, SafetyLevel level)
        {
            var names = assessments.Where(a => a.Verdict == verdict).Select(a => a.Item.Entry.Name).ToArray();
            if (names.Length > 0)
                advice.Add(new SystemAdvice(id, AdviceModule.Startup, FindingSeverity.Info, level, [string.Join(NameSeparator, names)]));
        }
    }

    private static StartupVerdict Verdict(StartupItem item, StartupThresholds thresholds, ServiceCatalog catalog)
    {
        var entry = item.Entry;
        if (entry.State == StartupState.Disabled)
            return StartupVerdict.Disabled;
        if (entry.Location is StartupLocation.UserRunOnce or StartupLocation.MachineRunOnce || entry.ExecutablePath is null
            || entry.State == StartupState.Unknown)
        {
            return StartupVerdict.Unknown;
        }

        if (IsProtected(entry, catalog))
            return StartupVerdict.Protected;
        if (item.PrivateBytes is not { } bytes)
            return StartupVerdict.NotRunning;
        if (bytes < thresholds.MinPrivateMemoryMb * BytesPerMb)
            return StartupVerdict.Light;
        return entry.Location is StartupLocation.MachineRun or StartupLocation.MachineRun32 or StartupLocation.CommonFolder
            ? StartupVerdict.NeedsAdmin
            : StartupVerdict.Candidate;
    }

    // Цель ярлыка тоже проверяется: имя ярлыка в папке «Автозагрузка» выбирает установщик и может ничего не говорить.
    private static bool IsProtected(StartupEntry entry, ServiceCatalog catalog) =>
        catalog.SecurityKeywords.Concat(catalog.AntiCheatKeywords).Any(keyword =>
            entry.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || entry.Command.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || (entry.ExecutablePath?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false));
}
