namespace Booster.Core.Abstractions;

/// <summary>Заданный размер файла подкачки, МБ.</summary>
/// <param name="InitialSizeMb">0 вместе с MaximumSizeMb = 0 — размер этого файла выбирает система.</param>
public sealed record PagefileSetting(string Path, int InitialSizeMb, int MaximumSizeMb);

/// <summary>Текущий файл подкачки, МБ.</summary>
/// <param name="PeakUsageMb">Пик занятости с загрузки системы.</param>
public sealed record PagefileUsage(string Path, int AllocatedMb, int CurrentUsageMb, int PeakUsageMb);

/// <param name="AutomaticManaged">Размером всех файлов управляет система.</param>
/// <param name="Settings">Явно заданные файлы; при автоматическом управлении обычно пусто.</param>
public sealed record PagefileConfiguration(Reading<bool> AutomaticManaged, IReadOnlyList<PagefileSetting> Settings,
    IReadOnlyList<PagefileUsage> Usage);

/// <summary>Настройки и занятость файлов подкачки. Только чтение.</summary>
public interface IPagefileSource
{
    Task<PagefileConfiguration> ReadAsync(CancellationToken ct);
}
