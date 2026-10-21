using System.Runtime.InteropServices;
using Booster.Core.Inventory;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D12;
using Windows.Win32.Graphics.Dxgi;

namespace Booster.Platform.Windows.Hardware;

/// <summary>
/// Перечисляет адаптеры DXGI. Только чтение: фабрика и временные устройства D3D12 живут внутри процесса
/// и освобождаются сразу после запроса.
/// </summary>
public sealed class DxgiAdapterSource : IDxgiAdapterSource
{
    public const string Source = "DXGI";

    public unsafe IReadOnlyList<DxgiAdapterRecord> Enumerate()
    {
        // CreateDXGIFactory2 есть с Windows 8.1; приложение рассчитано на Windows 10/11.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
            throw new PlatformNotSupportedException("DXGI 1.3 requires Windows 10 or later");

        var factoryId = typeof(IDXGIFactory1).GUID;
        PInvoke.CreateDXGIFactory2(0, &factoryId, out var factoryObject).ThrowOnFailure();
        try
        {
            var factory = (IDXGIFactory1)factoryObject;
            var ranks = HighPerformanceRanks(factoryObject);
            var adapters = new List<DxgiAdapterRecord>();
            for (uint i = 0; ; i++)
            {
                var hr = factory.EnumAdapters1(i, out var adapter);
                if (hr == HRESULT.DXGI_ERROR_NOT_FOUND)
                    break;
                hr.ThrowOnFailure();
                try
                {
                    var desc = adapter.GetDesc1();
                    var luid = new AdapterLuid(desc.AdapterLuid.LowPart, desc.AdapterLuid.HighPart);
                    adapters.Add(new DxgiAdapterRecord(
                        Text(desc.Description.AsReadOnlySpan()),
                        desc.VendorId, desc.DeviceId, desc.SubSysId, desc.Revision, luid,
                        desc.DedicatedVideoMemory, desc.SharedSystemMemory,
                        desc.Flags.HasFlag(DXGI_ADAPTER_FLAG.DXGI_ADAPTER_FLAG_SOFTWARE),
                        ranks.TryGetValue(luid, out var rank) ? rank : -1,
                        QueryUma(adapter)));
                }
                finally
                {
                    Marshal.ReleaseComObject(adapter);
                }
            }

            return adapters;
        }
        finally
        {
            Marshal.ReleaseComObject(factoryObject);
        }
    }

    /// <summary>Порядок адаптеров «от самого производительного»; пусто, если IDXGIFactory6 нет (до Windows 10 1803).</summary>
    private static unsafe Dictionary<AdapterLuid, int> HighPerformanceRanks(object factoryObject)
    {
        var ranks = new Dictionary<AdapterLuid, int>();
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17134) || factoryObject is not IDXGIFactory6 factory6)
            return ranks;

        var adapterId = typeof(IDXGIAdapter1).GUID;
        for (uint i = 0; ; i++)
        {
            void* pointer;
            try
            {
                factory6.EnumAdapterByGpuPreference(i, DXGI_GPU_PREFERENCE.DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE, &adapterId,
                    out pointer);
            }
            catch (COMException ex) when (ex.HResult == HRESULT.DXGI_ERROR_NOT_FOUND.Value)
            {
                return ranks;
            }

            var adapter = (IDXGIAdapter1)Marshal.GetObjectForIUnknown((nint)pointer);
            Marshal.Release((nint)pointer);
            try
            {
                var luid = adapter.GetDesc1().AdapterLuid;
                ranks.TryAdd(new AdapterLuid(luid.LowPart, luid.HighPart), (int)i);
            }
            finally
            {
                Marshal.ReleaseComObject(adapter);
            }
        }
    }

    /// <summary><c>D3D12_FEATURE_DATA_ARCHITECTURE.UMA</c>; null — адаптер не поддерживает D3D12 или D3D12 нет.</summary>
    private static unsafe bool? QueryUma(IDXGIAdapter1 adapter)
    {
        try
        {
            if (PInvoke.D3D12CreateDevice(adapter, D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0, out ID3D12Device device).Failed)
                return null;
            try
            {
                var architecture = new D3D12_FEATURE_DATA_ARCHITECTURE { NodeIndex = 0 };
                device.CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_ARCHITECTURE, &architecture,
                    (uint)sizeof(D3D12_FEATURE_DATA_ARCHITECTURE));
                return architecture.UMA;
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException or InvalidCastException)
        {
            return null;
        }
    }

    private static string Text(ReadOnlySpan<char> buffer)
    {
        var end = buffer.IndexOf('\0');
        return (end < 0 ? buffer : buffer[..end]).ToString().Trim();
    }
}
