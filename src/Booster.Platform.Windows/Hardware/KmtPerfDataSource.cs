using Booster.Core.Inventory;
using Windows.Wdk.Graphics.Direct3D;
using Windows.Win32.Foundation;
using Kmt = Windows.Wdk.PInvoke;

namespace Booster.Platform.Windows.Hardware;

/// <summary>
/// Читает <c>KMTQAITYPE_ADAPTERPERFDATA</c> и <c>…_CAPS</c> физического адаптера 0. Только чтение;
/// дескриптор из D3DKMTOpenAdapterFromLuid закрывается в любом случае.
/// </summary>
public sealed class KmtPerfDataSource : IKmtPerfDataSource
{
    private const string OpenFunction = "D3DKMTOpenAdapterFromLuid";
    private const string PerfDataFunction = "D3DKMTQueryAdapterInfo(KMTQAITYPE_ADAPTERPERFDATA)";

    public unsafe KmtPerfData Read(AdapterLuid luid)
    {
        var open = new D3DKMT_OPENADAPTERFROMLUID { AdapterLuid = new LUID { LowPart = luid.LowPart, HighPart = luid.HighPart } };
        var status = Kmt.D3DKMTOpenAdapterFromLuid(&open).Value;
        if (status < 0)
            return KmtPerfDataParser.FromStatus(OpenFunction, status);

        try
        {
            D3DKMT_ADAPTER_PERFDATA data = default;
            status = Query(open.hAdapter, KMTQUERYADAPTERINFOTYPE.KMTQAITYPE_ADAPTERPERFDATA, &data, (uint)sizeof(D3DKMT_ADAPTER_PERFDATA));
            if (status < 0)
                return KmtPerfDataParser.FromStatus(PerfDataFunction, status);

            // Отказ CAPS не портит основные данные: без него неизвестен только предел температуры.
            D3DKMT_ADAPTER_PERFDATACAPS caps = default;
            var capsStatus = Query(open.hAdapter, KMTQUERYADAPTERINFOTYPE.KMTQAITYPE_ADAPTERPERFDATA_CAPS, &caps,
                (uint)sizeof(D3DKMT_ADAPTER_PERFDATACAPS));

            return KmtPerfDataParser.Parse(data.MemoryFrequency, data.FanRPM, data.Temperature, data.PowerStateOverride != 0,
                capsStatus < 0 ? null : (caps.TemperatureMax, caps.TemperatureWarning));
        }
        finally
        {
            var close = new D3DKMT_CLOSEADAPTER { hAdapter = open.hAdapter };
            Kmt.D3DKMTCloseAdapter(&close);
        }
    }

    // PhysicalAdapterIndex у обеих структур — первое поле и остаётся 0: адаптер без связки (linked) имеет один физический.
    private static unsafe int Query(uint adapter, KMTQUERYADAPTERINFOTYPE type, void* buffer, uint size)
    {
        var query = new D3DKMT_QUERYADAPTERINFO
        {
            hAdapter = adapter,
            Type = type,
            pPrivateDriverData = buffer,
            PrivateDriverDataSize = size,
        };
        return Kmt.D3DKMTQueryAdapterInfo(&query).Value;
    }
}
