using System.Globalization;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Booster.Core.Inventory;
using Booster.Core.Reports;

namespace Booster.Core.SystemHealth;

/// <summary>
/// Рекомендации по памяти и файлу подкачки. Все — только вручную (L3): Booster не меняет подкачку и настройки памяти.
/// Система работает штатно — рекомендаций нет.
/// </summary>
public static class MemoryAdvisor
{
    private const string PathSeparator = ", ";

    /// <summary>
    /// Режим по настройкам, а не по текущим файлам: заданное, но ещё не применённое (ждёт перезагрузки) уже определяет режим.
    /// </summary>
    public static Reading<PagefileMode> Mode(Reading<PagefileConfiguration> pagefile)
    {
        if (!pagefile.IsAvailable)
            return new Reading<PagefileMode>(default, pagefile.Status, pagefile.Source, pagefile.Note);

        var automatic = pagefile.Value!.AutomaticManaged;
        if (!automatic.IsAvailable)
            return new Reading<PagefileMode>(default, automatic.Status, automatic.Source, automatic.Note);

        var settings = pagefile.Value.Settings;
        var mode = automatic.Value ? PagefileMode.SystemManaged
            : settings.Count == 0 ? PagefileMode.Disabled
            : settings.All(s => s.InitialSizeMb == 0 && s.MaximumSizeMb == 0) ? PagefileMode.SystemManagedSize
            : PagefileMode.Manual;
        return Reading<PagefileMode>.Known(mode, automatic.Source ?? pagefile.Source ?? string.Empty);
    }

    public static IReadOnlyList<SystemAdvice> Advise(Reading<MemoryStatus> memory, Reading<PagefileConfiguration> pagefile,
        HardwareInventory hardware, MemoryThresholds thresholds, double lowFreeSpacePercent)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        ArgumentNullException.ThrowIfNull(thresholds);

        var advice = new List<SystemAdvice>();
        var mode = Mode(pagefile);

        if (mode.IsAvailable && mode.Value == PagefileMode.Disabled)
            advice.Add(Advice(AdviceId.PagefileDisabled, FindingSeverity.Warning));
        if (mode.IsAvailable && mode.Value == PagefileMode.Manual)
            advice.Add(Advice(AdviceId.PagefileManual, FindingSeverity.Info,
                string.Join(PathSeparator, pagefile.Value!.Settings.Select(s => s.Path))));

        AddCommit(advice, memory, thresholds);

        var volumes = PagefileVolumes(pagefile);
        var devices = hardware.Storage.IsAvailable ? hardware.Storage.Value! : [];
        if (mode.IsAvailable && mode.Value is PagefileMode.SystemManaged or PagefileMode.SystemManagedSize)
        {
            foreach (var letter in volumes)
            {
                if (FindVolume(devices, letter) is { } volume && FreePercent(volume.Volume) is { } free && free < lowFreeSpacePercent)
                    advice.Add(Advice(AdviceId.PagefileVolumeLowSpace, FindingSeverity.Warning, letter));
            }
        }

        var hasFastVolume = devices.Any(d => d.Media.IsAvailable && d.Media.Value is StorageMediaKind.Ssd or StorageMediaKind.Scm
            && d.Volumes.Count > 0);
        foreach (var letter in volumes)
        {
            if (hasFastVolume && FindVolume(devices, letter) is { } volume
                && volume.Device.Media.IsAvailable && volume.Device.Media.Value == StorageMediaKind.Hdd)
                advice.Add(Advice(AdviceId.PagefileOnHdd, FindingSeverity.Info, letter));
        }

        if (hardware.Memory.IsAvailable)
            AddModules(advice, hardware.Memory.Value!.Modules);

        // Сортировка устойчива: внутри важности остаётся порядок правил.
        return advice.OrderByDescending(a => a.Severity).ToArray();
    }

    private static void AddCommit(List<SystemAdvice> advice, Reading<MemoryStatus> memory, MemoryThresholds thresholds)
    {
        if (!memory.IsAvailable || memory.Value!.CommitLimitBytes <= 0)
            return;

        var status = memory.Value;
        var current = Percent(status.CommitTotalBytes, status.CommitLimitBytes);
        var peak = Percent(status.CommitPeakBytes, status.CommitLimitBytes);
        if (current >= thresholds.CommitPercent)
            advice.Add(Advice(AdviceId.CommitNearLimit, FindingSeverity.Warning, WholePercent(current)));
        else if (peak >= thresholds.CommitPercent)
            advice.Add(Advice(AdviceId.CommitPeakNearLimit, FindingSeverity.Info, WholePercent(peak)));
    }

    private static void AddModules(List<SystemAdvice> advice, IReadOnlyList<MemoryModule> modules)
    {
        // Только частоты модуля: режим каналов не измеряется и здесь не используется.
        var slow = modules.FirstOrDefault(m => m.RatedSpeedMts.IsAvailable && m.ConfiguredSpeedMts.IsAvailable
            && m.ConfiguredSpeedMts.Value < m.RatedSpeedMts.Value);
        if (slow is not null)
            advice.Add(Advice(AdviceId.MemoryBelowRatedSpeed, FindingSeverity.Info,
                slow.ConfiguredSpeedMts.Value.ToString(CultureInfo.InvariantCulture),
                slow.RatedSpeedMts.Value.ToString(CultureInfo.InvariantCulture)));

        if (modules.Count == 1)
            advice.Add(Advice(AdviceId.SingleMemoryModule, FindingSeverity.Info));

        if (modules.Count >= 2)
        {
            var capacities = modules.Where(m => m.CapacityBytes.IsAvailable).Select(m => m.CapacityBytes.Value).Distinct().Count();
            var parts = modules.Where(m => m.PartNumber.IsAvailable)
                .Select(m => m.PartNumber.Value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            if (capacities > 1 || parts > 1)
                advice.Add(Advice(AdviceId.MixedMemoryModules, FindingSeverity.Info));
        }
    }

    /// <summary>Буквы томов файлов подкачки: из текущих файлов, а если их нет — из настроек.</summary>
    private static IReadOnlyList<string> PagefileVolumes(Reading<PagefileConfiguration> pagefile)
    {
        if (!pagefile.IsAvailable)
            return [];

        var configuration = pagefile.Value!;
        var paths = configuration.Usage.Count > 0
            ? configuration.Usage.Select(u => u.Path)
            : configuration.Settings.Select(s => s.Path);
        return paths
            .Select(VolumeLetter)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>«C:\pagefile.sys» → «C:»; «?:\pagefile.sys» (на всех томах) и прочее → null.</summary>
    internal static string? VolumeLetter(string path) =>
        path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':' ? $"{char.ToUpperInvariant(path[0])}:" : null;

    internal static (StorageDeviceInfo Device, VolumeInfo Volume)? FindVolume(IReadOnlyList<StorageDeviceInfo> devices, string letter)
    {
        foreach (var device in devices)
        {
            foreach (var volume in device.Volumes)
            {
                if (string.Equals(volume.DriveLetter, letter, StringComparison.OrdinalIgnoreCase))
                    return (device, volume);
            }
        }
        return null;
    }

    private static double? FreePercent(VolumeInfo volume) =>
        volume.SizeBytes.IsAvailable && volume.FreeBytes.IsAvailable && volume.SizeBytes.Value > 0
            ? Percent(volume.FreeBytes.Value, volume.SizeBytes.Value)
            : null;

    private static double Percent(long part, long whole) => (double)part / whole * 100;

    private static string WholePercent(double percent) => Math.Floor(percent).ToString(CultureInfo.InvariantCulture);

    private static SystemAdvice Advice(AdviceId id, FindingSeverity severity, params string[] arguments) =>
        new(id, AdviceModule.Memory, severity, SafetyLevel.L3ManualOnly, arguments);
}
