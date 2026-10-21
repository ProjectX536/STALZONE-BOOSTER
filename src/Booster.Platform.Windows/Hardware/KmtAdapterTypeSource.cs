using Booster.Core.Inventory;
using Windows.Wdk.Graphics.Direct3D;
using Kmt = Windows.Wdk.PInvoke;

namespace Booster.Platform.Windows.Hardware;

/// <summary>
/// Читает <c>D3DKMT_ADAPTERTYPE</c> у всех адаптеров графического ядра. Открытые D3DKMTEnumAdapters2
/// дескрипторы закрываются в любом случае.
/// </summary>
public sealed class KmtAdapterTypeSource : IKmtAdapterTypeSource
{
    public const string Source = "D3DKMTQueryAdapterInfo(KMTQAITYPE_ADAPTERTYPE)";

    // Между вызовом за количеством и вызовом за данными адаптер может появиться: повторяем запрос.
    private const int MaxAttempts = 3;
    private const int StatusBufferTooSmall = unchecked((int)0xC0000023);

    public unsafe IReadOnlyDictionary<AdapterLuid, KmtAdapterFlags> Query()
    {
        var adapters = EnumerateAdapters();
        var result = new Dictionary<AdapterLuid, KmtAdapterFlags>();
        try
        {
            foreach (var adapter in adapters)
            {
                D3DKMT_ADAPTERTYPE type = default;
                var query = new D3DKMT_QUERYADAPTERINFO
                {
                    hAdapter = adapter.hAdapter,
                    Type = KMTQUERYADAPTERINFOTYPE.KMTQAITYPE_ADAPTERTYPE,
                    pPrivateDriverData = &type,
                    PrivateDriverDataSize = (uint)sizeof(D3DKMT_ADAPTERTYPE),
                };
                if (Kmt.D3DKMTQueryAdapterInfo(&query).Value < 0)
                    continue;

                result[new AdapterLuid(adapter.AdapterLuid.LowPart, adapter.AdapterLuid.HighPart)] =
                    new KmtAdapterFlags(type.HybridDiscrete, type.HybridIntegrated, type.SoftwareDevice);
            }
        }
        finally
        {
            foreach (var adapter in adapters)
            {
                var close = new D3DKMT_CLOSEADAPTER { hAdapter = adapter.hAdapter };
                Kmt.D3DKMTCloseAdapter(&close);
            }
        }

        return result;
    }

    private static unsafe D3DKMT_ADAPTERINFO[] EnumerateAdapters()
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var count = new D3DKMT_ENUMADAPTERS2();
            ThrowIfFailed(Kmt.D3DKMTEnumAdapters2(&count).Value, "D3DKMTEnumAdapters2");

            var buffer = new D3DKMT_ADAPTERINFO[count.NumAdapters];
            if (buffer.Length == 0)
                return buffer;

            fixed (D3DKMT_ADAPTERINFO* pointer = buffer)
            {
                var request = new D3DKMT_ENUMADAPTERS2 { NumAdapters = (uint)buffer.Length, pAdapters = pointer };
                var status = Kmt.D3DKMTEnumAdapters2(&request).Value;
                if (status == StatusBufferTooSmall)
                    continue;
                ThrowIfFailed(status, "D3DKMTEnumAdapters2");
                return buffer[..(int)request.NumAdapters];
            }
        }

        throw new InvalidOperationException("D3DKMTEnumAdapters2: adapter list kept changing");
    }

    private static void ThrowIfFailed(int status, string function)
    {
        if (status < 0)
            throw new InvalidOperationException($"{function} failed: NTSTATUS 0x{unchecked((uint)status):X8}");
    }
}
