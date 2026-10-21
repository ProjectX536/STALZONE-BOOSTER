using System.ComponentModel;

namespace Booster.Core.Abstractions;

public sealed record MemoryStatus(long PhysicalTotalBytes, long PhysicalAvailableBytes, long CommitTotalBytes, long CommitLimitBytes,
    long CommitPeakBytes, long SystemCacheBytes, long KernelPagedBytes, long KernelNonPagedBytes, int ProcessCount, int ThreadCount,
    int HandleCount);

public interface IMemoryStatusSource
{
    MemoryStatus Read();
}
