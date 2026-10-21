using System.Globalization;
using System.Runtime.InteropServices;

namespace Booster.Platform.Windows.Monitoring;

public sealed record ProcessTimes(int ProcessId, string Name, long CreateTime, long CpuTime100ns, IReadOnlyList<ThreadTimes> Threads,
    ProcessCounters Counters);

public sealed record ProcessCounters(long WorkingSetBytes, long PrivateBytes, long HardFaultCount, long ReadBytes, long WriteBytes)
{
    public static readonly ProcessCounters Zero = new(0, 0, 0, 0, 0);
}

public sealed record ThreadTimes(int ThreadId, long CpuTime100ns);

public static partial class SystemProcessInformation
{
    public const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    private const int SystemProcessInformationClass = 5;
    private const int IdleProcessId = 0;
    private const string IdleName = "Idle";

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    internal struct ProcessEntry
    {
        [FieldOffset(0)] public uint NextEntryOffset;
        [FieldOffset(4)] public uint NumberOfThreads;
        [FieldOffset(16)] public uint HardFaultCount;
        [FieldOffset(32)] public long CreateTime;
        [FieldOffset(40)] public long UserTime;
        [FieldOffset(48)] public long KernelTime;
        [FieldOffset(56)] public ushort ImageNameLength;
        [FieldOffset(64)] public nint ImageNameBuffer;
        [FieldOffset(80)] public nint UniqueProcessId;
        [FieldOffset(144)] public nuint WorkingSetSize;
        [FieldOffset(184)] public nuint PagefileUsage;
        [FieldOffset(232)] public long ReadTransferCount;
        [FieldOffset(240)] public long WriteTransferCount;
    }

    [StructLayout(LayoutKind.Explicit, Size = 80)]
    internal struct ThreadEntry
    {
        [FieldOffset(0)] public long KernelTime;
        [FieldOffset(8)] public long UserTime;
        [FieldOffset(48)] public nint UniqueThread;
    }

    public static unsafe IReadOnlyList<ProcessTimes> Parse(ReadOnlySpan<byte> buffer)
    {
        var processSize = sizeof(ProcessEntry);
        var threadSize = sizeof(ThreadEntry);
        var processes = new List<ProcessTimes>();
        fixed (byte* start = buffer)
        {
            long offset = 0;
            while (true)
            {
                if (offset + processSize > buffer.Length)
                    throw new InvalidDataException($"Process entry at {offset} is outside the {buffer.Length}-byte buffer.");
                var entry = MemoryMarshal.Read<ProcessEntry>(buffer[(int)offset..]);

                var threadsStart = offset + processSize;
                if (threadsStart + (long)entry.NumberOfThreads * threadSize > buffer.Length)
                    throw new InvalidDataException($"Threads of the process entry at {offset} are outside the buffer.");
                var threads = new ThreadTimes[entry.NumberOfThreads];
                for (var i = 0; i < threads.Length; i++)
                {
                    var thread = MemoryMarshal.Read<ThreadEntry>(buffer[(int)(threadsStart + (long)i * threadSize)..]);
                    threads[i] = new ThreadTimes((int)thread.UniqueThread, thread.KernelTime + thread.UserTime);
                }

                var processId = (int)entry.UniqueProcessId;
                processes.Add(new ProcessTimes(processId, ReadName(buffer, start, entry, processId), entry.CreateTime,
                    entry.KernelTime + entry.UserTime, threads,
                    new ProcessCounters((long)entry.WorkingSetSize, (long)entry.PagefileUsage, entry.HardFaultCount,
                        entry.ReadTransferCount, entry.WriteTransferCount)));

                if (entry.NextEntryOffset == 0)
                    return processes;
                offset += entry.NextEntryOffset;
            }
        }
    }

    internal static unsafe int Query(byte* buffer, int length, out int written)
    {
        var status = NtQuerySystemInformation(SystemProcessInformationClass, buffer, (uint)length, out var returned);
        written = (int)Math.Min(returned, (uint)length);
        return status;
    }

    private static unsafe string ReadName(ReadOnlySpan<byte> buffer, byte* start, in ProcessEntry entry, int processId)
    {
        if (entry.ImageNameBuffer == 0 || entry.ImageNameLength == 0)
            return processId == IdleProcessId ? IdleName : processId.ToString(CultureInfo.InvariantCulture);

        var nameOffset = (long)((byte*)entry.ImageNameBuffer - start);
        if (nameOffset < 0 || nameOffset + entry.ImageNameLength > buffer.Length)
            throw new InvalidDataException($"Image name of process {processId} is outside the buffer.");

        var name = new string(MemoryMarshal.Cast<byte, char>(buffer.Slice((int)nameOffset, entry.ImageNameLength & ~1)));
        return name[(name.LastIndexOf('\\') + 1)..];
    }

    [LibraryImport("ntdll.dll")]
    private static unsafe partial int NtQuerySystemInformation(int systemInformationClass, void* systemInformation,
        uint systemInformationLength, out uint returnLength);
}
