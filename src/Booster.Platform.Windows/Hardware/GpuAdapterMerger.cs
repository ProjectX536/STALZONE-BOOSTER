using Booster.Core.Abstractions;
using Booster.Core.Inventory;
using Booster.Platform.Windows.Common;

namespace Booster.Platform.Windows.Hardware;

public static class GpuAdapterMerger
{
    public const string OrderMatchNote = "сопоставлено по порядку";

    private const string Wmi = "WMI Win32_VideoController";
    private const string Dxgi = "DXGI DXGI_ADAPTER_DESC1";
    private const string RankSource = "DXGI IDXGIFactory6.EnumAdapterByGpuPreference(HIGH_PERFORMANCE)";
    private const string KmtSource = "D3DKMT_ADAPTERTYPE";
    private const string UmaSource = "D3D12 FEATURE_DATA_ARCHITECTURE.UMA";
    private const string AdapterListSource = "DXGI + WMI adapter list";

    private static readonly IReadOnlyDictionary<AdapterLuid, KmtAdapterFlags> NoFlags = new Dictionary<AdapterLuid, KmtAdapterFlags>();

    private readonly record struct WmiEntry(CimRow Row, PciId? Pci);

    public static GpuInventory Merge(Reading<IReadOnlyList<CimRow>> wmiControllers, Reading<IReadOnlyList<DxgiAdapterRecord>> dxgi,
        Reading<IReadOnlyDictionary<AdapterLuid, KmtAdapterFlags>> kmt, Reading<IReadOnlyList<AppGpuPreference>> preferences)
    {
        var wmiEntries = (wmiControllers.IsAvailable ? wmiControllers.Value! : [])
            .Select(row => new WmiEntry(row, PciVendors.TryParsePnpId(row.GetString("PNPDeviceID"), out var pci) ? pci : null))
            .ToList();
        var records = dxgi.IsAvailable ? dxgi.Value! : [];
        var flags = kmt.IsAvailable ? kmt.Value! : NoFlags;

        var used = new bool[wmiEntries.Count];
        var adapters = new List<GpuAdapterInfo>();
        foreach (var record in records)
        {
            var index = -1;
            for (var i = 0; i < wmiEntries.Count && index < 0; i++)
            {
                if (!used[i] && SameDevice(wmiEntries[i].Pci, record))
                    index = i;
            }

            CimRow? row = null;
            string? note = null;
            if (index >= 0)
            {
                used[index] = true;
                row = wmiEntries[index].Row;
                // Одинаковые карты неразличимы по VEN/DEV/SUBSYS: пары составлены по порядку перечисления.
                if (records.Count(r => SameIds(r, record)) > 1 || wmiEntries.Count(e => SameDevice(e.Pci, record)) > 1)
                    note = OrderMatchNote;
            }

            adapters.Add(FromDxgi(record, row, note, wmiControllers, flags));
        }

        for (var i = 0; i < wmiEntries.Count; i++)
        {
            if (!used[i])
                adapters.Add(FromWmiOnly(wmiEntries[i], dxgi));
        }

        return new GpuInventory(adapters, DetectHybrid(adapters, flags), preferences);
    }

    private static GpuAdapterInfo FromDxgi(DxgiAdapterRecord record, CimRow? row, string? note,
        Reading<IReadOnlyList<CimRow>> wmi, IReadOnlyDictionary<AdapterLuid, KmtAdapterFlags> flags)
    {
        var isSoftware = record.IsSoftware || record.VendorId == PciVendors.Microsoft;
        var wmiMissing = wmi.IsAvailable ? "no matching WMI adapter" : Unavailable("WMI", wmi);

        return new GpuAdapterInfo(
            OemPlaceholders.Clean(row?.GetString("Name")) ?? record.Description.Trim(),
            PciVendors.FromId(record.VendorId),
            record.VendorId <= ushort.MaxValue && record.DeviceId <= ushort.MaxValue
                ? Reading<PciId>.Known(new PciId((ushort)record.VendorId, (ushort)record.DeviceId, record.SubSysId,
                    (byte)record.Revision), Dxgi)
                : Reading<PciId>.Unknown(Dxgi, "vendor/device id out of PCI range"),
            Reading<AdapterLuid>.Known(record.Luid, $"{Dxgi}.AdapterLuid"),
            isSoftware ? Reading<GpuAdapterKind>.Unknown(Dxgi, "software adapter") : KindOf(record, flags),
            Reading<long>.Known(ToLong(record.DedicatedVideoMemory), $"{Dxgi}.DedicatedVideoMemory"),
            Reading<long>.Known(ToLong(record.SharedSystemMemory), $"{Dxgi}.SharedSystemMemory"),
            row is null ? Reading<string>.Unknown($"{Wmi}.DriverVersion", wmiMissing) : DriverVersion(row),
            row is null ? Reading<DateOnly>.Unknown($"{Wmi}.DriverDate", wmiMissing) : DriverDate(row),
            record.HighPerformanceRank >= 0
                ? Reading<int>.Known(record.HighPerformanceRank, RankSource)
                : Reading<int>.Unknown(RankSource, "IDXGIFactory6 unavailable"),
            isSoftware,
            note);
    }

    private static GpuAdapterInfo FromWmiOnly(WmiEntry entry, Reading<IReadOnlyList<DxgiAdapterRecord>> dxgi)
    {
        var dxgiMissing = dxgi.IsAvailable ? "no matching DXGI adapter" : Unavailable("DXGI", dxgi);

        return new GpuAdapterInfo(
            OemPlaceholders.Clean(entry.Row.GetString("Name")) ?? "",
            entry.Pci is { } pci ? PciVendors.FromId(pci.VendorId) : GpuVendor.Unknown,
            entry.Pci is { } id
                ? Reading<PciId>.Known(id, $"{Wmi}.PNPDeviceID")
                : Reading<PciId>.Unknown($"{Wmi}.PNPDeviceID", "not a PCI device"),
            Reading<AdapterLuid>.Unknown($"{Dxgi}.AdapterLuid", dxgiMissing),
            Reading<GpuAdapterKind>.Unknown(UmaSource, dxgiMissing),
            Reading<long>.Unknown($"{Dxgi}.DedicatedVideoMemory", dxgiMissing),
            Reading<long>.Unknown($"{Dxgi}.SharedSystemMemory", dxgiMissing),
            DriverVersion(entry.Row),
            DriverDate(entry.Row),
            Reading<int>.Unknown(RankSource, dxgiMissing),
            IsSoftware: entry.Pci is null || entry.Pci.Value.VendorId == PciVendors.Microsoft,
            MatchNote: null);
    }

    private static Reading<GpuAdapterKind> KindOf(DxgiAdapterRecord record, IReadOnlyDictionary<AdapterLuid, KmtAdapterFlags> flags)
    {
        if (flags.TryGetValue(record.Luid, out var f) && f.HybridIntegrated)
            return Reading<GpuAdapterKind>.Known(GpuAdapterKind.Integrated, $"{KmtSource}.HybridIntegrated");
        if (f is { HybridDiscrete: true })
            return Reading<GpuAdapterKind>.Known(GpuAdapterKind.Discrete, $"{KmtSource}.HybridDiscrete");
        return record.IsUma switch
        {
            true => Reading<GpuAdapterKind>.Known(GpuAdapterKind.Integrated, UmaSource),
            false => Reading<GpuAdapterKind>.Known(GpuAdapterKind.Discrete, UmaSource),
            null => Reading<GpuAdapterKind>.Unknown(UmaSource, "D3D12 unavailable for this adapter"),
        };
    }

    private static Reading<bool> DetectHybrid(IReadOnlyList<GpuAdapterInfo> adapters,
        IReadOnlyDictionary<AdapterLuid, KmtAdapterFlags> flags)
    {
        if (flags.Values.Any(f => !f.SoftwareDevice && (f.HybridDiscrete || f.HybridIntegrated)))
            return Reading<bool>.Known(true, KmtSource);

        var hardware = adapters.Where(a => !a.IsSoftware).ToList();
        var kinds = hardware.Where(a => a.Kind.IsAvailable).Select(a => a.Kind.Value).ToHashSet();
        if (kinds.Contains(GpuAdapterKind.Integrated) && kinds.Contains(GpuAdapterKind.Discrete))
            return Reading<bool>.Known(true, UmaSource);

        return hardware.Count == 1
            ? Reading<bool>.Known(false, AdapterListSource)
            : Reading<bool>.Unknown(AdapterListSource, $"{hardware.Count} hardware adapters, kinds not determined");
    }

    private static Reading<string> DriverVersion(CimRow row) =>
        OemPlaceholders.Clean(row.GetString("DriverVersion")) is { } version
            ? Reading<string>.Known(version, $"{Wmi}.DriverVersion")
            : Reading<string>.Unknown($"{Wmi}.DriverVersion");

    private static Reading<DateOnly> DriverDate(CimRow row) =>
        DmtfDate.ParseDate(row.GetString("DriverDate")) is { } date
            ? Reading<DateOnly>.Known(date, $"{Wmi}.DriverDate")
            : Reading<DateOnly>.Unknown($"{Wmi}.DriverDate");

    private static bool SameDevice(PciId? pci, DxgiAdapterRecord record) =>
        pci is { } p && p.VendorId == record.VendorId && p.DeviceId == record.DeviceId && p.SubsystemId == record.SubSysId;

    private static bool SameIds(DxgiAdapterRecord a, DxgiAdapterRecord b) =>
        a.VendorId == b.VendorId && a.DeviceId == b.DeviceId && a.SubSysId == b.SubSysId;

    private static long ToLong(ulong value) => value > long.MaxValue ? long.MaxValue : (long)value;

    private static string Unavailable<T>(string name, Reading<T> source) =>
        source.Note is { } note ? $"{name} unavailable ({source.Status}): {note}" : $"{name} unavailable ({source.Status})";
}
