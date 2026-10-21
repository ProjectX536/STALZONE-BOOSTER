using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Booster.Platform.Windows.Monitoring;

namespace Booster.Platform.Windows.Tests.Monitoring;

public class SystemProcessInformationTests
{
    private const int ProcessSize = 256;
    private const int ThreadSize = 80;

    private sealed record SyntheticThread(int Id, long Kernel, long User);

    private sealed record SyntheticCounters(uint HardFaults, long WorkingSet, long Pagefile, long Read, long Write);

    private sealed record SyntheticProcess(int Pid, string? Name, long CreateTime, long Kernel, long User, params SyntheticThread[] Threads)
    {
        public SyntheticCounters Counters { get; init; } = new(0, 0, 0, 0, 0);
    }

    private static readonly SyntheticProcess Idle =
        new(0, null, 0, 50_000_000, 0, new SyntheticThread(0, 25_000_000, 0), new SyntheticThread(0, 25_000_000, 0));

    private static readonly SyntheticProcess Game = new(4242, "game.exe", 133_000_000_000, 2_000_000, 8_000_000,
        new SyntheticThread(100, 1_000_000, 5_000_000), new SyntheticThread(101, 500_000, 2_000_000), new SyntheticThread(102, 500_000, 1_000_000));

    private static IReadOnlyList<ProcessTimes> Parse(SyntheticProcess[] processes, int lengthChange = 0)
    {
        var buffer = new byte[64 * 1024];
        buffer.AsSpan().Fill(0xCD);
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var baseAddress = handle.AddrOfPinnedObject();
            var offset = 0;
            for (var i = 0; i < processes.Length; i++)
            {
                var process = processes[i];
                var nameOffset = ProcessSize + ThreadSize * process.Threads.Length;
                var name = Encoding.Unicode.GetBytes(process.Name ?? string.Empty);
                var size = (nameOffset + name.Length + 7) / 8 * 8;
                var entry = buffer.AsSpan(offset, size);
                entry.Clear();

                BinaryPrimitives.WriteUInt32LittleEndian(entry[0..], i == processes.Length - 1 ? 0u : (uint)size);
                BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], (uint)process.Threads.Length);
                BinaryPrimitives.WriteInt64LittleEndian(entry[32..], process.CreateTime);
                BinaryPrimitives.WriteInt64LittleEndian(entry[40..], process.User);
                BinaryPrimitives.WriteInt64LittleEndian(entry[48..], process.Kernel);
                if (process.Name is not null)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(entry[56..], (ushort)name.Length);
                    BinaryPrimitives.WriteUInt16LittleEndian(entry[58..], (ushort)(name.Length + 2));
                    BinaryPrimitives.WriteInt64LittleEndian(entry[64..], baseAddress + offset + nameOffset);
                    name.CopyTo(entry[nameOffset..]);
                }
                BinaryPrimitives.WriteInt64LittleEndian(entry[80..], process.Pid);
                BinaryPrimitives.WriteUInt32LittleEndian(entry[16..], process.Counters.HardFaults);
                BinaryPrimitives.WriteInt64LittleEndian(entry[144..], process.Counters.WorkingSet);
                BinaryPrimitives.WriteInt64LittleEndian(entry[184..], process.Counters.Pagefile);
                BinaryPrimitives.WriteInt64LittleEndian(entry[232..], process.Counters.Read);
                BinaryPrimitives.WriteInt64LittleEndian(entry[240..], process.Counters.Write);

                for (var t = 0; t < process.Threads.Length; t++)
                {
                    var thread = entry[(ProcessSize + ThreadSize * t)..];
                    BinaryPrimitives.WriteInt64LittleEndian(thread[0..], process.Threads[t].Kernel);
                    BinaryPrimitives.WriteInt64LittleEndian(thread[8..], process.Threads[t].User);
                    BinaryPrimitives.WriteInt64LittleEndian(thread[40..], process.Pid);
                    BinaryPrimitives.WriteInt64LittleEndian(thread[48..], process.Threads[t].Id);
                }
                offset += size;
            }
            return SystemProcessInformation.Parse(buffer.AsSpan(0, offset + lengthChange));
        }
        finally
        {
            handle.Free();
        }
    }

    [Fact]
    public void Struct_sizes_match_x64_layout()
    {
        Assert.Equal(ProcessSize, Unsafe.SizeOf<SystemProcessInformation.ProcessEntry>());
        Assert.Equal(ThreadSize, Unsafe.SizeOf<SystemProcessInformation.ThreadEntry>());
    }

    [Fact]
    public void Parse_reads_processes_threads_and_names_from_synthetic_buffer()
    {
        var processes = Parse([Idle, Game]);

        Assert.Equal(2, processes.Count);
        var idle = processes[0];
        Assert.Equal(0, idle.ProcessId);
        Assert.Equal("Idle", idle.Name);
        Assert.Equal(50_000_000, idle.CpuTime100ns);
        Assert.Equal(2, idle.Threads.Count);

        var game = processes[1];
        Assert.Equal(4242, game.ProcessId);
        Assert.Equal("game.exe", game.Name);
        Assert.Equal(133_000_000_000, game.CreateTime);
        Assert.Equal(10_000_000, game.CpuTime100ns);
        Assert.Equal([new ThreadTimes(100, 6_000_000), new ThreadTimes(101, 2_500_000), new ThreadTimes(102, 1_500_000)], game.Threads);
    }

    [Fact]
    public void Parses_memory_and_io_counters()
    {
        var game = Game with { Counters = new SyntheticCounters(HardFaults: 7, WorkingSet: 1_048_576, Pagefile: 2_097_152, Read: 4096, Write: 8192) };

        var parsed = Parse([Idle, game])[1];

        Assert.Equal(new ProcessCounters(WorkingSetBytes: 1_048_576, PrivateBytes: 2_097_152, HardFaultCount: 7, ReadBytes: 4096, WriteBytes: 8192),
            parsed.Counters);
    }

    [Fact]
    public void Parse_stops_at_zero_next_offset_and_rejects_truncated_buffer()
    {
        Assert.Equal(2, Parse([Idle, Game], lengthChange: 512).Count);

        Assert.Throws<InvalidDataException>(() => Parse([Idle, Game], lengthChange: -(ThreadSize + 40)));
    }

    [Fact]
    public void Empty_buffer_is_rejected() =>
        Assert.Throws<InvalidDataException>(() => SystemProcessInformation.Parse([]));
}
