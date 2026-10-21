using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.SystemInformation;

namespace Booster.Platform.Windows.Hardware;

/// <summary>Топология ядер через GetLogicalProcessorInformationEx(RelationProcessorCore).</summary>
public sealed class CpuTopologySource : ICpuTopologySource
{
    public const string Source = "GetLogicalProcessorInformationEx(RelationProcessorCore).EfficiencyClass";

    public unsafe IReadOnlyList<byte> GetEfficiencyClassPerCore()
    {
        uint length = 0;
        // Первый вызов сообщает размер буфера; между вызовами набор ядер может измениться (горячее
        // подключение процессоров), поэтому повторяем, пока буфера хватает не впритык.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (!PInvoke.GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore, null, &length))
            {
                var error = (WIN32_ERROR)Marshal.GetLastPInvokeError();
                if (error != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
                    throw new Win32Exception((int)error);
            }

            var buffer = new byte[length];
            fixed (byte* start = buffer)
            {
                if (PInvoke.GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore,
                        (SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX*)start, &length))
                    return Parse(start, length);

                var error = (WIN32_ERROR)Marshal.GetLastPInvokeError();
                if (error != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
                    throw new Win32Exception((int)error);
            }
        }

        throw new Win32Exception((int)WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER);
    }

    // Записи переменной длины: каждая начинается с Relationship и Size, следующая — через Size байт.
    private static unsafe byte[] Parse(byte* start, uint length)
    {
        var classes = new List<byte>();
        for (uint offset = 0; offset < length;)
        {
            var info = (SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX*)(start + offset);
            if (info->Size == 0 || offset + info->Size > length)
                throw new InvalidDataException($"Malformed processor information record at offset {offset}");
            if (info->Relationship == LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore)
                classes.Add(info->Anonymous.Processor.EfficiencyClass);
            offset += info->Size;
        }

        return classes.ToArray();
    }
}
