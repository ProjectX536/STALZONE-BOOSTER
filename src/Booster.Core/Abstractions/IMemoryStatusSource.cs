using System.ComponentModel;

namespace Booster.Core.Abstractions;

/// <summary>Память системы одним вызовом, в байтах.</summary>
/// <param name="CommitTotalBytes">Выделено (commit) всеми процессами и ядром.</param>
/// <param name="CommitLimitBytes">Предел выделения: физическая память + текущий размер файлов подкачки.</param>
/// <param name="CommitPeakBytes">Пик выделения с загрузки системы.</param>
public sealed record MemoryStatus(long PhysicalTotalBytes, long PhysicalAvailableBytes, long CommitTotalBytes, long CommitLimitBytes,
    long CommitPeakBytes, long SystemCacheBytes, long KernelPagedBytes, long KernelNonPagedBytes, int ProcessCount, int ThreadCount,
    int HandleCount);

public interface IMemoryStatusSource
{
    /// <exception cref="Win32Exception">Сбой вызова.</exception>
    MemoryStatus Read();
}
