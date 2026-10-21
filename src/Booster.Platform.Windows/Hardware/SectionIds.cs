using Booster.Core.Engine;

namespace Booster.Platform.Windows.Hardware;

/// <summary>Идентификаторы секций: ключи таймаутов в settings.json и имена секций в логах и отчёте.</summary>
public static class SectionIds
{
    public const string Cpu = "cpu";
    public const string Gpu = "gpu";
    public const string Memory = "memory";
    public const string Board = "board";
    public const string Os = "os";
    public const string Storage = "storage";
    public const string Displays = "displays";
    public const string Drivers = SnapshotCollector.DriversSectionId;
}
