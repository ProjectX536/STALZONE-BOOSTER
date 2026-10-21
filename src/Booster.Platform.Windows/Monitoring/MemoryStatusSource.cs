using System.ComponentModel;
using System.Runtime.InteropServices;
using Booster.Core.Abstractions;
using Windows.Win32;
using Windows.Win32.System.ProcessStatus;

namespace Booster.Platform.Windows.Monitoring;

/// <summary>Память системы через <c>GetPerformanceInfo</c>: значения в страницах переводятся в байты.</summary>
public sealed class MemoryStatusSource : IMemoryStatusSource
{
    public const string Source = "GetPerformanceInfo";

    public unsafe MemoryStatus Read()
    {
        PERFORMANCE_INFORMATION info;
        if (!PInvoke.GetPerformanceInfo(&info, (uint)sizeof(PERFORMANCE_INFORMATION)))
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        var page = (long)info.PageSize;
        return new MemoryStatus(
            Bytes(info.PhysicalTotal, page), Bytes(info.PhysicalAvailable, page),
            Bytes(info.CommitTotal, page), Bytes(info.CommitLimit, page), Bytes(info.CommitPeak, page),
            Bytes(info.SystemCache, page), Bytes(info.KernelPaged, page), Bytes(info.KernelNonpaged, page),
            (int)info.ProcessCount, (int)info.ThreadCount, (int)info.HandleCount);
    }

    private static long Bytes(nuint pages, long pageSize) => checked((long)pages * pageSize);
}
