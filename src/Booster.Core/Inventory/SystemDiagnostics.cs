using Booster.Core.Abstractions;

namespace Booster.Core.Inventory;

/// <summary>
/// Системная диагностика снимка: память и подкачка, процессы с наибольшей памятью, службы, журнал стабильности, сетевые
/// адаптеры, автозагрузка, игровые настройки Windows, запущенные программы с оверлеем и HAGS. Каждая секция собирается отдельно:
/// сбой одной не мешает остальным.
/// </summary>
/// <param name="TopMemoryProcesses">По убыванию частной памяти, не больше <c>diagnostics.topProcessCount</c>.</param>
/// <param name="Stability">События за <c>diagnostics.stabilityDays</c> дней.</param>
/// <param name="Overlays">В порядке каталога; только запущенные.</param>
/// <param name="Hags">Значение источника; сбой или таймаут секции — её статус с источником <c>hags</c>.</param>
public sealed record SystemDiagnostics(
    Reading<MemoryStatus> Memory,
    Reading<PagefileConfiguration> Pagefile,
    Reading<IReadOnlyList<ProcessMemoryUsage>> TopMemoryProcesses,
    Reading<IReadOnlyList<ServiceInfo>> Services,
    Reading<StabilityLog> Stability,
    Reading<IReadOnlyList<NetworkAdapterInfo>> NetworkAdapters,
    Reading<IReadOnlyList<StartupItem>> Startup,
    Reading<GamingSettings> Gaming,
    Reading<IReadOnlyList<RunningOverlay>> Overlays,
    Reading<bool> Hags);

/// <param name="PrivateBytes">Сумма частной памяти процессов с именем exe элемента; null — процесс не запущен или exe неизвестен.</param>
public sealed record StartupItem(StartupEntry Entry, long? PrivateBytes);

/// <summary>Запущенная программа с внутриигровым оверлеем. Включён ли сам оверлей, по процессу не узнать.</summary>
/// <param name="Process">Как в каталоге.</param>
/// <param name="ProcessIds">Запущенные экземпляры.</param>
public sealed record RunningOverlay(string Process, string Name, IReadOnlyList<int> ProcessIds);
