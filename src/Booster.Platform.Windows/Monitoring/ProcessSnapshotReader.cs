using System.Globalization;
using System.Runtime.InteropServices;

namespace Booster.Platform.Windows.Monitoring;

internal sealed class ProcessSnapshotReader : IDisposable
{
    private const int InitialBufferBytes = 512 * 1024;
    private const int MaxBufferBytes = 64 * 1024 * 1024;

    private unsafe byte* _buffer;
    private int _bufferBytes;

    public unsafe IReadOnlyList<ProcessTimes> Read()
    {
        if (_buffer is null)
            Allocate(InitialBufferBytes);

        while (true)
        {
            var status = SystemProcessInformation.Query(_buffer, _bufferBytes, out var written);
            if (status >= 0)
                return SystemProcessInformation.Parse(new ReadOnlySpan<byte>(_buffer, written));
            if (status != SystemProcessInformation.StatusInfoLengthMismatch)
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
                    $"NtQuerySystemInformation failed with status 0x{status:X8}."));
            if (_bufferBytes >= MaxBufferBytes)
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
                    $"Process snapshot does not fit into {MaxBufferBytes / (1024 * 1024)} MB."));
            Allocate(_bufferBytes * 2);
        }
    }

    public void Dispose() => FreeBuffer();

    private unsafe void Allocate(int bytes)
    {
        FreeBuffer();
        _buffer = (byte*)NativeMemory.Alloc((nuint)bytes);
        _bufferBytes = bytes;
    }

    private unsafe void FreeBuffer()
    {
        NativeMemory.Free(_buffer);
        _buffer = null;
        _bufferBytes = 0;
    }
}
